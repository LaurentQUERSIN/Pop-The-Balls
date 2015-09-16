using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stormancer;
using Stormancer.Core;

namespace Pop_The_Balls
{
    public class app
    {
        public void Run(IAppBuilder builder)
        {
            builder.SceneTemplate("main", scene => new Main(scene));
        }

    }
}
