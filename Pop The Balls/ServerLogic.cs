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

        private ConcurrentDictionary<long, Player> _players = new ConcurrentDictionary<long, Player>();
        private ConcurrentDictionary<int, Ball> _balls = new ConcurrentDictionary<int, Ball>();

        public main(ISceneHost scene)
        {
            _scene = scene;
            _scene.GetComponent<ILogger>().Debug("server", "starting configuration");
            _env = _scene.GetComponent<IEnvironment>();

            _scene.Connecting.Add(onConnecting);
            _scene.Connected.Add(onConnected);
            _scene.Disconnected.Add(onDisconnected);

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
            if (_isRunning == false)
                throw new ClientException("le serveur est vérouillé.");
            else if (_players.Count >= 100)
                throw new ClientException("le serveur est complet.");
            return Task.FromResult(true);
        }

        private Task onConnected(IScenePeerClient client)
        {
            _scene.GetComponent<ILogger>().Debug("main", "new client connecting");
            Player player = new Player(client.GetUserData<string>());
            if (_players.Count < 100)
            {
                _scene.GetComponent<ILogger>().Debug("main", "client connected with name : " + player.name);
                _players.TryAdd(client.Id, player);
            }
            return Task.FromResult(true);
        }

        private Task onDisconnected(DisconnectedArgs arg)
        {
            if(_players.ContainsKey(arg.Peer.Id))
            {
                Player temp;
                _scene.GetComponent<ILogger>().Debug("main", _players[arg.Peer.Id] + " s'est déconnecté (" + arg.Reason + ")");
                _players.TryRemove(arg.Peer.Id, out temp);
            }
            return Task.FromResult(true);
        }

        private Task onClick(RequestContext<IScenePeerClient> ctx)
        {
            if (_players.ContainsKey(ctx.RemotePeer.Id))
            {
                var reader = new BinaryReader(ctx.ReadObject<Stream>());
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                long timestamp = reader.ReadInt32();

                foreach(Ball ball in _balls.Values)
                {
                    Ball temp;
                    if (ball.IsClicked(x, y, timestamp))
                    {
                        ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(1);});
                        _scene.Broadcast("destroy_ball", s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(ball.id); }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_SEQUENCED);
                        _balls.TryRemove(ball.id, out temp);
                    }
                }
            }
            return Task.FromResult(true);
        }

        private Task onUpdateLeaderBoard(RequestContext<IScenePeerClient> ctx)
        {
            List<Player> playerList = _players.Values.ToList();
            playerList.OrderBy(x => x.score);
            LeaderBoardDtO board = new LeaderBoardDtO();

            int i = 0;
            while (i < playerList.Count && i < 5)
            {
                board.names[i] = playerList[i].name;
                board.scores[i] = playerList[i].score.ToString();
                i++;
            }
            while (i < 5)
            {
                board.names[i] = "";
                board.scores[i] = "";
                i++;
            }
            board.localNbr = _players[ctx.RemotePeer.Id].score.ToString();
            ctx.SendValue(board);
            return Task.FromResult(true);
        }

        private void runLogic()
        {
            _isRunning = true;
            long lastUpdate = _env.Clock;
            while (_isRunning == true)
            {
                if (lastUpdate + 100 < _env.Clock)
                {
                    lastUpdate = _env.Clock;
                    Random rand = new Random();
                    float x = (float)((rand.NextDouble() - 0.5) * 24);
                    float vx = (float)((rand.NextDouble() - 0.5) * 12);
                    Ball newBall = new Ball(_ids, _env.Clock, x, 6f, vx, -2f);
                    _scene.Broadcast("create_ball", s =>
                    {
                        var writer = new BinaryWriter(s, Encoding.UTF8, false);
                        writer.Write(x);
                        writer.Write(6f);
                        writer.Write(vx);
                        writer.Write(-2f);
                    }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                    _balls.TryAdd(_ids, newBall);
                    _ids++;
                    foreach (Ball ball in _balls.Values)
                    {
                        Ball temp;
                        if (ball.creationTime + 5000 > _env.Clock)
                        {
                            _scene.Broadcast("destroy_ball", s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(ball.id); }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_SEQUENCED);
                            _balls.TryRemove(ball.id, out temp);
                        } }
                    if (_ids >= 2000000)
                        _ids = 0;
                    _scene.GetComponent<ILogger>().Debug("main", "reseting Ids to avoid overflow");
                }
            }
        }
    }
}