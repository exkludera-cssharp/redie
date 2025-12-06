using CounterStrikeSharp.API.Core;

public class Config : BasePluginConfig
{
    public string Prefix { get; set; } = "{purple}[Redie]{grey}";
    public List<string> Commands { get; set; } = ["css_redie", "css_ghost"];
    public bool Messages { get; set; } = true;
    public string Message_Redie { get; set; } = "You have been respawned as a ghost";
    public string Message_UnRedie { get; set; } = "You are no longer a ghost";
}
