using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Microsoft.Xna.Framework;
using Terraria.GameContent.Events;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Org.BouncyCastle.Asn1.Cms;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace TestPlugin
{
    [ApiVersion(2, 1)]//api版本
    public class TestPlugin : TerrariaPlugin
    {
        public override string Author => "GK 阁下";// 插件作者
        public override string Description => "Ping服务器来获取服务器延迟";// 插件说明
        public override string Name => "Ping";// 插件名字
        public override Version Version => new Version(1, 0, 0, 0);// 插件版本

        public TestPlugin(Main game) : base(game)// 插件处理
        {
            Order = 1000;//或者顺序一定要在最后
            PlayerPing = new PingData[256];
        }

        public override void Initialize()// 插件启动时，用于初始化各种狗子
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);//钩住游戏初始化时
            ServerApi.Hooks.NetGetData.Register(this, Hook_Ping_GetData);

        }
        protected override void Dispose(bool disposing)// 插件关闭时
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);//销毁游戏初始化狗子
                ServerApi.Hooks.NetGetData.Deregister(this, Hook_Ping_GetData);

            }
            base.Dispose(disposing);
        }
        private void OnInitialize(EventArgs args)//游戏初始化的狗子
        {
            Commands.ChatCommands.Add(new Command("", PingC, "ping") { HelpText = "输入/ping 可以查看自己到服务器的网络延迟" });
        }
  
        private async void PingC(CommandArgs args)//异步指令
        {
            try
            {
                if (PlayerPing[args.Player.Index]!=null)
                {
                    args.Player.SendErrorMessage("已在 Ping 中...");
                    return;
                }
                PlayerPing[args.Player.Index]= new PingData();
                var player = args.Player;
                var result = await Ping(player);
                player.SendSuccessMessage($"您的Ping为: {result.TotalMilliseconds:F1}ms");
            }
            catch (Exception e)
            {
                args.Player.SendErrorMessage("Ping 失败.");
                TShockAPI.TShock.Log.Error(e.ToString());
            }
            PlayerPing[args.Player.Index] = null;
        }

        private PingData[] PlayerPing { get; set; }
        public class PingData
        {
            public TimeSpan? LastPing;//可空
            internal PingDetails?[] RecentPings = new PingDetails?[Terraria.Main.item.Length];//可空
        }
        internal class PingDetails
        {
            internal Channel<int>? Channel;
            internal DateTime Start = DateTime.Now;
            internal DateTime? End = null;
        }

        public async Task<TimeSpan> Ping(TSPlayer player)//异步了个ping
        {
            return await Ping(player, new CancellationTokenSource(1000).Token);
        }

        public async Task<TimeSpan> Ping(TSPlayer player, CancellationToken token)
        {
            var pingdata = PlayerPing[player.Index];//取得玩家ping数据
            if(pingdata==null) return TimeSpan.MaxValue;//没有时返回最大

            var inv = -1;//物品序号
            for (var i = 0; i < Terraria.Main.item.Length; i++)
                if (Terraria.Main.item[i] != null)
                    if (!Terraria.Main.item[i].active || Terraria.Main.item[i].playerIndexTheItemIsReservedFor == 255)
                    {
                        if (pingdata.RecentPings[i]?.Channel == null)//意味着必须有凋落物才行吗?
                        {
                            inv = i;
                            break;//一次针对一个凋落物,共四百多个挂起
                        }
                    }

            if (inv == -1) return TimeSpan.MaxValue;//没有时返回最大

            var pd = pingdata.RecentPings[inv] ??= new PingDetails();//玩家数据??用于排除空,从中二选一
            
            pd.Channel ??= Channel.CreateBounded<int>(new BoundedChannelOptions(30)
            {
                SingleReader = true,
                SingleWriter = true
            });//创建用于等待的通道

 
            Terraria.NetMessage.TrySendData((int)PacketTypes.RemoveItemOwner, player.Index, -1, null, inv);//发送消息

            await pd.Channel.Reader.ReadAsync(token);//等待出现一个数据
            pd.Channel = null;

            return (pingdata.LastPing = pd.End!.Value - pd.Start).Value;//返回延迟
        }

        private void Hook_Ping_GetData(GetDataEventArgs args)
        {
            if (args.Handled) return;
            if (args.MsgID != PacketTypes.ItemOwner) return;
            var user = TShock.Players[args.Msg.whoAmI];
            if (user == null) return;
            using (BinaryReader date = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length)))
            {
                int iid = date.ReadInt16();
                int pid = date.ReadByte();
                if(pid!=255) return;//排除吸到的 //取到物品的ID,此值255为无人
                var pingresponse = PlayerPing[args.Msg.whoAmI];
                var ping = pingresponse?.RecentPings[iid];
                if (ping != null)//利用同步
                {
                    ping.End = DateTime.Now;
                    ping.Channel!.Writer.TryWrite(iid);//通道写入
                }
            }

        }


    }


   
}
