using Microsoft.Xna.Framework;

namespace RegionVision
{
    public class RegionData
    {
        public string Name { get; set; }

        public Rectangle Area { get; set; }

        public RegionData(string name, Rectangle area)
        {
            Name = name;
            Area = area;
        }
    }
}
