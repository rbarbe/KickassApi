using System;

namespace Kickass.KickassApi.Models
{
    public class Torrent
    {
        private Uri _magnet;
        public int Id { get; set; }
        public string Hash { get; set; }
        public string Name { get; set; }
        public Uri Uri { get; set; }
        public double Size { get; set; }
        public int Files { get; set; }
        public TimeSpan Age { get; set; }
        public int Seed { get; set; }
        public int Leech { get; set; }

        public Uri Magnet
        {
            get
            {
                if (_magnet == null && !string.IsNullOrWhiteSpace(Hash))
                {
                    return new Uri("magnet:?xt = urn:btih:" + Hash, UriKind.Absolute);
                }
                return _magnet;
            }
            set { _magnet = value; }
        }

        public Status Status { get; set; }
        public string DeletedBy { get; set; }
        private DateTimeOffset DeletedOn { get; set; }
        public Uri DeletedByProfileUri { get; set; }
    }
}