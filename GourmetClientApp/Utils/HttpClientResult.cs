using System.Net.Http;

namespace GourmetClientApp.Utils;

public record HttpClientResult<T>(HttpClient Client, T ResponseResult);