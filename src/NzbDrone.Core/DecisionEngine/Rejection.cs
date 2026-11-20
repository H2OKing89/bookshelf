namespace NzbDrone.Core.DecisionEngine
{
    public class Rejection
    {
        private string _code = RejectionCode.Unknown;

        public string Reason { get; set; }

        public string Code
        {
            get => _code;
            set => _code = string.IsNullOrWhiteSpace(value) ? RejectionCode.Unknown : value;
        }

        public RejectionType Type { get; set; }

        public Rejection()
            : this(null)
        {
        }

        public Rejection(string reason, RejectionType type = RejectionType.Permanent, string code = RejectionCode.Unknown)
        {
            Reason = reason;
            Type = type;
            Code = code;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", Type, Reason);
        }
    }
}
