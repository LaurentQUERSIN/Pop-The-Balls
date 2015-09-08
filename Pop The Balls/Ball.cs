using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public bool IsClicked(float player_x, float player_y, long time)
        {
            float updated_x = x + (vx * (time - creationTime));
            float updated_y = y + (vy * (time - creationTime));

            if (updated_x - 0.3f < player_x && player_x < updated_x + 0.3f && updated_y - 0.3f < player_y && player_y < updated_y + 0.3f)
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
