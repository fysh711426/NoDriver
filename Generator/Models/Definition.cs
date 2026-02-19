namespace Generator.Models
{
    public class Definition
    {
        public Version Version { get; set; } = new();
        public List<Domain> Domains { get; set; } = new();
    }
}
