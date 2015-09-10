using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stormancer;
using Stormancer.Core;
using Stormancer.Diagnostics;

namespace Pop_The_Balls
{
    public class Ball
    {
        public int id;
        public long creationTime;

        public float x = 0;
        public float y = 0;
        public float vx =  0;
        public float vy = 0;

        public bool IsClicked(float player_x, float player_y, long time, ISceneHost scene)
        {
            float dist = 1.0f;

            float updated_x = x + (vx * (time - creationTime) / 1000);
            float updated_y = y + (vy * (time - creationTime) / 1000);

            scene.GetComponent<ILogger>().Debug("main", "ball_c : " + updated_x.ToString() + " " + updated_y.ToString() + " || player : " + player_x.ToString() + " " + player_y.ToString());
            if (updated_x - dist < player_x && player_x < updated_x + dist && updated_y - dist < player_y && player_y < updated_y + dist)
                return (true);
            return (false);
        }

        public Ball(int nid, long time, float px, float py, float nvx, float nvy)
        {
            id = nid;
            creationTime = time;
            x = px;
            y = py;
            vx = nvx;
            vy = nvy;
        }
    }
}
