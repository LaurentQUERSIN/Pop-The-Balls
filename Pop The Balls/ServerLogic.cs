using Stormancer;
using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer.Server.Components;
using Stormancer.Diagnostics;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Concurrency;

namespace Pop_The_Balls
{
    static class GameSceneExtensions
    {
        public static void AddGameScene(this IAppBuilder builder)
        {
            builder.SceneTemplate("main", scene => new main(scene));
        }
    }

    class main
    {
        private ISceneHost _scene;
        private IEnvironment _env;
        private bool _isRunning = false;
        private int _ids = 0;
        private int playersConnected = 0;

        private ConcurrentDictionary<long, Player> _players = new ConcurrentDictionary<long, Player>();
        private ConcurrentDictionary<int, Ball> _balls = new ConcurrentDictionary<int, Ball>();
        private Random _rand = new Random();

        private string version = "a0.3.2";

        public main(ISceneHost scene)
        {
            _scene = scene;
            _scene.GetComponent<ILogger>().Debug("server", "starting configuration");
            _env = _scene.GetComponent<IEnvironment>();

            _scene.Connecting.Add(onConnecting);
            _scene.Connected.Add(onConnected);
            _scene.Disconnected.Add(onDisconnected);

            _scene.AddProcedure("play", onPlay);
            _scene.AddProcedure("click", onClick);
            _scene.AddProcedure("update_leaderBoard", onUpdateLeaderBoard);

            _scene.Starting.Add(onStarting);
            _scene.Shuttingdown.Add(onShutdown);

            _scene.GetComponent<ILogger>().Debug("server", "configuration complete");

        }

        private Task onStarting(dynamic arg)
        {
            _scene.GetComponent<ILogger>().Debug("server", "starting game loop");
            _isRunning = true;
            runLogic();
            return Task.FromResult(true);
        }

        private Task onShutdown(ShutdownArgs arg)
        {
            return Task.FromResult(true);
        }

        private Task onConnecting(IScenePeerClient client)
        {
            _scene.GetComponent<ILogger>().Debug("main", "A new client try to connect");
            ConnectionDtO player_dto = client.GetUserData<ConnectionDtO>();
            if (_isRunning == false)
                throw new ClientException("le serveur est vérouillé.");
            else if (playersConnected >= 100)
                throw new ClientException("le serveur est complet.");
            else if (player_dto.version != version)
                throw new ClientException("mauvaise version");
            return Task.FromResult(true);
        }

        private Task onConnected(IScenePeerClient client)
        {
            playersConnected++;
            return Task.FromResult(true);
        }

        private Task onDisconnected(DisconnectedArgs arg)
        {
            if (_players.ContainsKey(arg.Peer.Id))
            {
                playersConnected--;
                Player temp;
                _scene.GetComponent<ILogger>().Debug("main", _players[arg.Peer.Id] + " s'est déconnecté (" + arg.Reason + ")");
                _players.TryRemove(arg.Peer.Id, out temp);
            }
            return Task.FromResult(true);
        }

        private Task onPlay(RequestContext<IScenePeerClient> ctx)
        {
            bool nameAlreadyTaken = false;
            string name = ctx.ReadObject<ConnectionDtO>().name.ToLower() ;
            foreach (Player p in _players.Values)
            {
                string temp = p.name.ToLower();
                if (temp == name)
                {
                    nameAlreadyTaken = true;
                    break;
                }

            }
            if (nameAlreadyTaken == false)
            {
                Player player = new Player(name);
                _scene.GetComponent<ILogger>().Debug("main", "client connected with name : " + player.name);
                _players.TryAdd(ctx.RemotePeer.Id, player);
                ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(0); });
            }
            else
                ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(1); });
            return Task.FromResult(true);
        }

        private Task onClick(RequestContext<IScenePeerClient> ctx)
        {
            if (_players.ContainsKey(ctx.RemotePeer.Id))
            {
                bool hitGoodBall = false;
                bool touched = false;
                var reader = new BinaryReader(ctx.InputStream);
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                long timestamp = reader.ReadInt32();
                Ball hitBall = null;
                Ball temp;
                Player player = _players[ctx.RemotePeer.Id];

                foreach (Ball ball in _balls.Values)
                {
                    if (ball.IsClicked(x, y, timestamp, _scene))
                    {
                        hitBall = ball;
                        touched = true;
                        if (((_env.Clock - hitBall.creationTime) / hitBall.oscillationTime) % 2 == 0)
                        {
                            hitGoodBall = true;
                            break;
                        }
                    }
                }
                if (hitBall != null)
                {
                    if (hitGoodBall)
                    {
                       player.score++;
                        player.streak++;
                        if (player.score > player.record)
                            player.record = player.score;
                        if (player.streak >= 5)
                        {
                            player.streak = 0;
                            player.life++;
                            if (player.life > 3)
                                player.life = 3;
                            ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(3); writer.Write(player.life); });
                        }
                        else
                            ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(1); });
                        _scene.Broadcast("destroy_ball", s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(hitBall.id); }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_SEQUENCED);
                        _balls.TryRemove(hitBall.id, out temp);
                    }
                    else
                    {
                        player.streak = 0;
                        player.life--;
                        ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(2); writer.Write(_players[ctx.RemotePeer.Id].life); });
                        _scene.Broadcast("destroy_ball", s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(hitBall.id); }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_SEQUENCED);
                        _balls.TryRemove(hitBall.id, out temp);
                        if (_players[ctx.RemotePeer.Id].life <= 0)
                        {
                            _players[ctx.RemotePeer.Id].life = 3;
                            _players[ctx.RemotePeer.Id].score = 0;
                        }
                    }
                }
                if (touched == false)
                    ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(0); });
            }
                return Task.FromResult(true);
        }

        private Task onUpdateLeaderBoard(RequestContext<IScenePeerClient> ctx)
        {
            List<Player> playerList = _players.Values.ToList();
            playerList.OrderByDescending(x => x.record);
            LeaderBoardDtO board = new LeaderBoardDtO();

            int i = 0;
            while (i < playerList.Count && i < 5)
            {
                board.names[i] = playerList[i].name;
                board.scores[i] = playerList[i].record.ToString();
                i++;
            }
            while (i < 5)
            {
                board.names[i] = "";
                board.scores[i] = "";
                i++;
            }
            if (_players.ContainsKey(ctx.RemotePeer.Id))
                board.localNbr = _players[ctx.RemotePeer.Id].score.ToString();
            ctx.SendValue(board);
            return Task.FromResult(true);
        }

        private async Task runLogic()
        {
            _isRunning = true;
            long lastUpdate = _env.Clock;


            while (_isRunning == true)
            {
                if (lastUpdate + 100 < _env.Clock)
                {
                    lastUpdate = _env.Clock;
                    Ball newBall = new Ball(_ids, _env.Clock, _rand);
                    _scene.Broadcast("create_ball", s =>
                    {
                        var writer = new BinaryWriter(s, Encoding.UTF8, false);
                        writer.Write(_ids);
                        writer.Write(newBall.x);
                        writer.Write(newBall.y);
                        writer.Write(newBall.vx);
                        writer.Write(newBall.vy);
                        writer.Write(_env.Clock);
                        writer.Write(newBall.oscillationTime);
                    }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                    //BallDtO data = new BallDtO(newBall);
                    //_scene.Broadcast("create_ball", ballDtO, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                    _balls.TryAdd(_ids, newBall);
                    _ids++;
                    foreach (Ball ball in _balls.Values)
                    {
                        Ball temp;
                        if (_env.Clock > ball.creationTime + 30000)
                        {
                            _scene.Broadcast("destroy_ball", s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(ball.id); }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                            _balls.TryRemove(ball.id, out temp);
                        }
                    }
                    if (_ids >= 2000000)
                    {
                        _ids = 0;
                        //_scene.GetComponent<ILogger>().Debug("main", "reseting Ids to avoid overflow");
                    }
                }
                await Task.Delay(100);
            }
        }
    }
}