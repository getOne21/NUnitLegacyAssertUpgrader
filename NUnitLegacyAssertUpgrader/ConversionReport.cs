internal partial class Program
{
    // =====================================================================
    // REPORT DTO
    // =====================================================================
    private record ConversionReport(int Processed, int Changed, double DurationSeconds)
    {
        public List<string> FilesChanged { get; set; } = new();
        public Dictionary<string, int> RuleStats { get; set; } = new();
    }
}
