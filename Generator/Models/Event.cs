namespace Generator.Models
{
    public class Event
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; } = null;
        public bool? Deprecated { get; set; } = null;
        public bool? Experimental { get; set; } = null;
        public List<Property> Parameters { get; set; } = new();
    }
}
