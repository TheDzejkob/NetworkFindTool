public class result
{
    public string Ip { get; set; }     // from ARP table
    public string Age { get; set; }    // from ARP table
    public string Vlan { get; set; }
    public string Mac { get; set; }
    public string Type { get; set; }   // f (DYNAMIC/STATIC)
    public string Port { get; set; }
    public string Status { get; set; } // "Connected" nebo "Disconnected"
    public string Name { get; set; }   // Interface name (e.g. Data & Voice)
}