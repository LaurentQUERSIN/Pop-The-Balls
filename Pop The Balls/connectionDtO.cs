using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pop_The_Balls
{
    class ConnectionDtO
    {
        public string name;
        public string version;

        public ConnectionDtO(string nm, string nv)
        {
            name = nm;
            version = nv;
        }
    }
}
