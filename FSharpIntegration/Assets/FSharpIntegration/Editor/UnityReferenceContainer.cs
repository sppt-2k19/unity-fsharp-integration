namespace FSharpIntegration.Editor
{
    class UnityReferenceContainer
    {
        public UnityReference UnityEngine { get; set; }
        public UnityReference UnityEditor { get; set; }
        public UnityReference CSharpDll { get; set; }
        public System.Collections.Generic.List<UnityReference> Additional { get; set; }
    }
}