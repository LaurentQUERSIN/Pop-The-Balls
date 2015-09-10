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
        public int oscillationTime;

        public float x = 0;
        public float y = 0;
        public float vx =  0;
        public float vy = 0;


        public bool IsClicked(float player_x, float player_y, long time, ISceneHost scene)
        {
            float dist = 0.5f;

            float updated_x = x + (vx * (time - creationTime) / 1000);
            float updated_y = y + (vy * (time - creationTime) / 1000);

            //scene.GetComponent<ILogger>().Debug("main", "ball_c : " + updated_x.ToString() + " " + updated_y.ToString() + " || player : " + player_x.ToString() + " " + player_y.ToString());
            if (updated_x - dist < player_x && player_x < updated_x + dist && updated_y - dist < player_y && player_y < updated_y + dist)
                return (true);
            return (false);
        }

        private void Normalize(ref double x, ref double y)
        {
            double length;

            length = Math.Sqrt((x * x) + (y * y));

            x = x / length;
            y = y / length;
        }

        private void RandomRingPosition(ref double x, ref double y, Random rand)
        {
            x = rand.NextDouble() - 0.5f;
            y = rand.NextDouble() - 0.5f;
            Normalize(ref x, ref y);
        }

        public Ball(int nid, long time, Random rand)
        {
            double tx = 0;
            double ty = 0;

            //randomizing spawn position
            RandomRingPosition(ref tx, ref ty, rand);
            x = (float) (tx) * 13f;
            y = (float) (ty) * 13f;

            //randomizing trajectory
            RandomRingPosition(ref tx, ref ty, rand);
            tx = (tx * 3f) - x;
            ty = (ty * 3f) - y;
            Normalize(ref tx, ref ty);
            vx = (float) tx;
            vy = (float) ty;

            oscillationTime = (rand.Next() % 5) * 1000;
            id = nid;
            creationTime = time;
        }
    }
}
