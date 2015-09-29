using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using ByteSizeLib;
using Kickass.KickassApi.Models;

namespace Kickass.KickassApi
{
    public class Api
    {
        private readonly Uri _baseUri;
        private HttpClient _httpClient;
        private HttpClientHandler _httpClientHandler;
        private string _username;

        public Api(Uri baseUri)
        {
            _baseUri = baseUri;
            if (!_baseUri.IsAbsoluteUri) throw new ArgumentException("Should be an AbsoluteUri", nameof(baseUri));
        }

        public Api()
        {
            _baseUri = new Uri("https://kat.cr", UriKind.Absolute);
        }

        public List<Torrent> UploadedTorrents
        {
            get
            {
                var torrents = new List<Torrent>(UploadedActiveTorrents);
                torrents.AddRange(UploadedDeletedTorrents);
                return torrents;
            }
        }

        public List<Torrent> UploadedActiveTorrents
        {
            get
            {
                var list = new List<Torrent>();
                var uri = new Uri("/user/" + _username + "/uploads/", UriKind.Relative);
                var response = _httpClient.GetAsync(uri).Result;
                var source = response.Content.ReadAsStringAsync().Result;

                var torrents = GetTorrents(source, Status.Active);

                return torrents;
            }
        }

        public List<Torrent> UploadedDeletedTorrents
        {
            get
            {
                var uri = new Uri("/user/" + _username + "/uploads/deleted/", UriKind.Relative);
                var response = _httpClient.GetAsync(uri).Result;
                var source = response.Content.ReadAsStringAsync().Result;

                var torrents = GetTorrents(source, Status.Deleted);

                return torrents;
            }
        }

        public async Task<byte[]> DownloadTorrentFile(Torrent torrent)
        {
            var uriStr = torrent.Uri.ToString();

            const string remove = ".html";

            if (uriStr.EndsWith(remove))
            {
                uriStr = uriStr.Substring(0, uriStr.LastIndexOf(remove, StringComparison.Ordinal));
            }

            var response = await _httpClient.GetByteArrayAsync(new Uri("/torrents" + uriStr, UriKind.Relative));
            return response;
        }

        private static TimeSpan ExtractAgeFromAgeCell(IHtmlTableCellElement cell)
        {
            var timeSpan = new TimeSpan();
            var content = cell.TextContent;
            var numberString = new string(content.ToCharArray().Where(char.IsDigit).ToArray());
            var number = int.Parse(numberString);

            if (content.Contains("year"))
            {
                timeSpan = TimeSpan.FromDays(number*365);
            }
            else if (content.Contains("month"))
            {
                timeSpan = TimeSpan.FromDays(number*31);
            }
            else if (content.Contains("day"))
            {
                timeSpan = TimeSpan.FromDays(number);
            }
            else if (content.Contains("hour"))
            {
                timeSpan = TimeSpan.FromHours(number);
            }
            else if (content.Contains("minute"))
            {
                timeSpan = TimeSpan.FromMinutes(number);
            }

            return timeSpan;
        }

        private static void ExtractFromTorrentNameCell(IHtmlTableCellElement cell, out string name, out Uri uri)
        {
            var torrentLink =
                cell.QuerySelector("div.torrentname").QuerySelector("a.cellMainLink") as IHtmlAnchorElement;
            name = torrentLink.Text;
            uri = new Uri(torrentLink.PathName, UriKind.Relative);
        }

        private static string ExtractHashFromControlCell(IHtmlTableCellElement cell)
        {
            var editLink = cell.QuerySelector("a[title=\"edit torrent\"]") as IHtmlAnchorElement;
            var splited = editLink.PathName.Split('/');
            return splited[3];
        }

        private static Uri ExtractMagnetFromTorrentNameCell(IHtmlTableCellElement cell)
        {
            var iconBox = cell.QuerySelector("div.iaconbox");
            var magnetLink = iconBox.QuerySelector("a.imagnet") as IHtmlAnchorElement;
            return new Uri(magnetLink.Href, UriKind.Absolute);
        }

        public async Task<EditTorrentFormContent> GetEditTorrentFormContent(Torrent torrent)
        {
            var editTorrentFormContent = new EditTorrentFormContent();
            var uri = new Uri("/torrents/edit/" + torrent.Hash + "/", UriKind.Relative);
            var response = await _httpClient.GetAsync(uri);
            var source = await response.Content.ReadAsStringAsync();
            var parser = new HtmlParser();
            var htmlDocument = parser.Parse(source);

            var categoryIdSelectTag = htmlDocument.QuerySelector("#categoryId") as IHtmlSelectElement;
            editTorrentFormContent.CategoryId = int.Parse(categoryIdSelectTag.Value);

            var subCategoryIdSelectTag = htmlDocument.QuerySelector("select[name=\"sub_cat\"]") as IHtmlSelectElement;
            editTorrentFormContent.SubCategoryId = int.Parse(subCategoryIdSelectTag.Value);

            var nameInputTag = htmlDocument.QuerySelector("input[name=\"name\"]") as IHtmlInputElement;
            editTorrentFormContent.Name = nameInputTag.Value;

            var bbcodeInput = htmlDocument.QuerySelector("#bbcode") as IHtmlTextAreaElement;
            editTorrentFormContent.Description = bbcodeInput.Value;

            return editTorrentFormContent;
        }

        private static List<Torrent> GetTorrents(string source, Status status)
        {
            var list = new List<Torrent>();
            var parser = new HtmlParser();
            var htmlDocument = parser.Parse(source);
            var torrentTable = htmlDocument.QuerySelector("table.data");

            var t = torrentTable as IHtmlTableElement;

            foreach (var tr in t.Rows)
            {
                if (tr.ClassName == "firstr") continue;

                var torrent = new Torrent {Status = status};

                var i = 0;
                foreach (var cell in tr.Cells)
                {
                    switch (i)
                    {
                        case 0:
                            string name;
                            Uri uri;
                            ExtractFromTorrentNameCell(cell, out name, out uri);
                            torrent.Name = name;
                            torrent.Uri = uri;

                            if (status == Status.Active)
                                torrent.Magnet = ExtractMagnetFromTorrentNameCell(cell);

                            break;

                        case 1:
                            torrent.Hash = ExtractHashFromControlCell(cell);
                            break;
                        case 2:
                            torrent.Size = ByteSize.Parse(cell.TextContent).Bytes;
                            break;
                        case 3:
                            torrent.Files = int.Parse(cell.TextContent);
                            break;
                        case 4:
                            torrent.Age = ExtractAgeFromAgeCell(cell);
                            break;
                        case 5:
                            torrent.Seed = int.Parse(cell.TextContent);
                            break;
                        case 6:
                            torrent.Leech = int.Parse(cell.TextContent);
                            break;
                        case 7:
                            var deletedByUser = cell.QuerySelector("a.plain") as IHtmlAnchorElement;
                            torrent.DeletedBy = deletedByUser.TextContent;
                            torrent.DeletedByProfileUri = new Uri(deletedByUser.PathName, UriKind.Relative);
                            break;
                    }
                    i++;
                }
                list.Add(torrent);
            }

            return list;
        }

        public async Task<bool> SignIn(string email, string password)
        {
            var result = false;

            _httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(_httpClientHandler) {BaseAddress = _baseUri};
            var uri = new Uri("/auth/login/", UriKind.Relative);

            var keyValues = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("email", email),
                new KeyValuePair<string, string>("password", password)
            };

            var formUrlEncodedContent = new FormUrlEncodedContent(keyValues);

            var req = new HttpRequestMessage(HttpMethod.Post, uri) {Content = formUrlEncodedContent};

            req.Headers.Referrer = _baseUri;

            var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var cookies = _httpClientHandler.CookieContainer.GetCookies(_baseUri).Cast<Cookie>();
                result =
                    cookies.Where(cookie => cookie.Name == "KAT_SESSION_LIVE")
                        .Any(cookie => !string.IsNullOrWhiteSpace(cookie.Value));

                if (result)
                {
                    var source = await response.Content.ReadAsStringAsync();
                    var parser = new HtmlParser();
                    var htmlDocument = parser.Parse(source);
                    var profileSpanTag = htmlDocument.QuerySelector("span.usernameProfile");
                    _username = profileSpanTag.InnerHtml;
                }
            }
            return result;
        }
    }

    public enum Status
    {
        Active,
        Deleted
    }
}