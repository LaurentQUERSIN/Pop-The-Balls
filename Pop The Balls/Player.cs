using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pop_The_Balls
{
    public class Player
    {
        public string name { get; set; } = "";
        public int score { get; set; } = 0;
        public int record { get; set; } = 0;
        public int streak { get; set; } = 0;

        private byte _life = 3;
        public byte Life
        {
            get
            {
                return _life;
            }
            set
            {
                _life = Math.Max(Math.Min(value, (byte)3), (byte)0);
            }
        }

       

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
