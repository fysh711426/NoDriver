namespace NoDriver.Core
{
    public record Position : Cdp.DOM.Quad
    {
        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }

        public double AbsX { get; set; } = 0.0;
        public double AbsY { get; set; } = 0.0;

        public double X => Left;
        public double Y => Top;
        public double Width => Right - Left;
        public double Height => Bottom - Top;
       
        public (double X, double Y) Center => (Left + (Width / 2.0), Top + (Height / 2.0));

        public Position(List<double> points) :
            base(points)
        {
            if (points == null || points.Count < 8)
                throw new ArgumentException("Points array must contain at least 8 elements representing the quad.");

            Left = points[0];
            Top = points[1];
            Right = points[2];
            // Top = points[3];
            // Right = points[4];
            Bottom = points[5];
            // Left = points[6];
            // Bottom = points[7];
        }

        public Cdp.Page.Viewport ToViewport(double scale = 1)
        {
            return new Cdp.Page.Viewport(X, Y, Width, Height, scale);
        }

        public override string ToString()
        {
            return $"<Position(x={Left}, y={Top}, width={Width}, height={Height})>";
        }
    }
}
