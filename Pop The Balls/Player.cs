using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pop_The_Balls
{
    public class Player
    {
        public string name;
        public int score = 0;
        public int record = 0;
        public int streak = 0;
        public byte life = 3;

        public Player(string nm)
        {
            name = nm;
        }

        public Player()
        {
            name = "";
        }
    }
}
