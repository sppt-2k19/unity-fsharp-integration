class Reference
{
    public Reference(string include, string hintPath)
    {
        Include = include;
        HintPath = hintPath;
    }

    public string Include { get; set; }
    public string HintPath { get; set; }

    public override int GetHashCode()
    {
        return Include.GetHashCode();
    }
}