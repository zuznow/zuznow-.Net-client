using System;
using System.Web;
using System.Web.SessionState;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Security;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Configuration;




public class MOBModule : IHttpModule, IRequiresSessionState
{

    string[] servers = GetConf("mob_servers", "").Split(',');
    string api_key = GetConf("mob_api_key", "");
    string domain_id = GetConf("mob_domain_id","");
    string cache_type = GetConf("mob_cache_type", "");
    string cache_ttl = GetConf("mob_cache_ttl", "");
    bool cache_ssl = Boolean.Parse(MOBModule.GetConf("mob_cache_ssl", "false"));

    private ResponseFilter _filter;
    private HttpApplication _context;

    public static string GetConf(string key,string def )
    {
        string val = ConfigurationManager.AppSettings[key];

        return String.IsNullOrEmpty(val) ? def : val ;
    }

    public static T[] Shuffle<T>(T[] array)
    {
        if (array.Length <= 1)
        {
            return array;
        }
        Random rnd = new Random();
        for (int i = array.Length; i > 1; i--)
        {
            int j = rnd.Next(i);
            T tmp = array[j];
            array[j] = array[i - 1];
            array[i - 1] = tmp;
        }
        return array;
    }

    public static bool MOBisTest(HttpApplication httpApplication)
    {

        string url = httpApplication.Context.Request.Url.ToString();
        Uri parsed_url = new Uri(url);
        if (parsed_url.Query != "")
        {
            NameValueCollection parsed_query = HttpUtility.ParseQueryString(parsed_url.Query);
            if (parsed_query["mobtest"] == "true")
            {
                HttpCookie newCookie = new HttpCookie("mobtest");
                newCookie.Value = "true";
                httpApplication.Context.Response.Cookies.Add(newCookie);
                httpApplication.Context.Request.Cookies["mobtest"].Value = "true";
                return true;
            }
            else if (parsed_query["mobtest"] == "false")
            {
                HttpCookie newCookie = new HttpCookie("mobtest");
                newCookie.Value = "false";
                httpApplication.Context.Response.Cookies.Add(newCookie);
                return false;
            }
        }
        if (httpApplication.Context.Request.Cookies["mobtest"] != null && httpApplication.Context.Request.Cookies["mobtest"].Value == "true")
        {
            return true;
        }
        return false;
    }

    public static string MOB_Get_UA(HttpApplication httpApplication)
    {


        string url = httpApplication.Context.Request.Url.ToString();
        Uri parsed_url = new Uri(url);
        if (parsed_url.Query != "")
        {
            NameValueCollection parsed_query = HttpUtility.ParseQueryString(parsed_url.Query);
            if (!String.IsNullOrEmpty(parsed_query["mob_ua"]))
            {
                HttpCookie newCookie = new HttpCookie("mob_ua");
                newCookie.Value = parsed_query["mob_ua"];
                httpApplication.Context.Response.Cookies.Add(newCookie);
                return parsed_query["mob_ua"];
            }
        }
        if (httpApplication.Context.Request.Cookies["mob_ua"] != null)
        {
            return httpApplication.Context.Request.Cookies["mob_ua"].Value;
        }
        return httpApplication.Context.Request.Headers["User-Agent"];
    }

    public bool MOBisSupported(HttpApplication httpApplication)
    {

        try
        {
            if (httpApplication.Request.Cookies["c2m_original"] != null && httpApplication.Request.Cookies["c2m_original"].Value == "true")
            {
                return false;
            }

            if (MOBisTest(httpApplication))
            {
                return true;
            }
            //return false;
            string user_agent = MOB_Get_UA(_context);
            if (user_agent == null)
            {
                return false;
            }
            bool isMobile = false;
            bool isTablet = false;
            bool isMobileApp = false;
            Match match = Regex.Match(user_agent, @"iPhone|iPod", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                isMobile = true;
            }
            match = Regex.Match(user_agent, @"Android", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                match = Regex.Match(user_agent, @"Mobile", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    isMobile = true;
                }
                else
                {
                    isTablet = true;
                }
            }
            match = Regex.Match(user_agent, @"ipad", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                isTablet = true;
            }
            match = Regex.Match(user_agent, @"MOBAPP", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                isMobileApp = true;
            }
            return isMobile || isTablet || isMobileApp;
            //return isMobileApp; //only mobile app
        }
        catch (Exception ex)
        {
            //string currentUrl = httpApplication.Context.Request.Url.ToString();
            //File.AppendAllText(@"c:\tmp\log.txt",ex.ToString());
            //File.AppendAllText(@"c:\tmp\log.txt",currentUrl+"\n");
            return false;
        }
    }

    public void Dispose()
    {
    }

    bool UseFilter()
    {
        string currentUrl = _context.Context.Request.Url.ToString().ToLower();
        if (currentUrl.IndexOf(".css") != -1 || currentUrl.IndexOf(".js") != -1 || currentUrl.IndexOf(".jpg") != -1 
			|| currentUrl.IndexOf(".gif") != -1 || currentUrl.IndexOf(".png") != -1
			|| currentUrl.IndexOf(".axd") != -1)
        {
            return false;
        }
        return true;
    }

    


    public bool MOB_Fetch_Cache(string cache_key)
    {
        
        string[] servers_urls = MOBModule.Shuffle(servers);
        string my_url = servers_urls[0] + "get_cached_data.php?key=" + api_key + "&domain_id=" + domain_id + "&cache_key=" + cache_key + "&user_agent=" + HttpUtility.UrlEncode(MOB_Get_UA(_context));
        if (cache_ttl != "")
        {
            my_url += "&cache_ttl=" + cache_ttl;
        }
        else
        {
            //if cache was set to anonymous  we must send a ttl value get_cached_data.php will not read config from dashboard, so send 15 min (same as dashboard default)
            my_url += "&cache_ttl=15";
        }
        HttpWebResponse httpGetWebResponse = null;
        try
        {
            HttpWebRequest httpGetRequest = (HttpWebRequest)WebRequest.Create(my_url);
            httpGetRequest.Method = "GET";
            httpGetRequest.AllowAutoRedirect = false;

            httpGetWebResponse = (HttpWebResponse)httpGetRequest.GetResponse();
        }
        catch (WebException ex)
        {
            return false;
        }
        if (httpGetWebResponse != null && httpGetWebResponse.StatusCode == HttpStatusCode.OK)
        {
            string charset = httpGetWebResponse.CharacterSet;
            StreamReader streamReader = new StreamReader(httpGetWebResponse.GetResponseStream(), Encoding.GetEncoding(charset), false);
            string data = streamReader.ReadToEnd();
            _context.Context.Response.Charset = charset;
            _context.Context.Response.ContentEncoding = Encoding.GetEncoding(charset);
            _context.Context.Response.Write(data);
            _context.Context.Response.Flush();
            _context.Context.Response.End();
            return true;
        }
        return false;
    }

    public static string GetMd5Hash(byte[] input)
    {
        MD5 md5Hash = MD5.Create();
        byte[] data = md5Hash.ComputeHash(input);
        // Create a new Stringbuilder to collect the bytes
        // and create a string.
        StringBuilder sBuilder = new StringBuilder();

        // Loop through each byte of the hashed data 
        // and format each one as a hexadecimal string.
        for (int i = 0; i < data.Length; i++)
        {
            sBuilder.Append(data[i].ToString("x2"));
        }

        // Return the hexadecimal string.
        return sBuilder.ToString();
    }

    public static bool MOB_Is_Secure_Connection(HttpApplication httpApplication)
    {
        if (httpApplication.Context.Request.IsSecureConnection)
        {
            return true;
        }
        string proto = httpApplication.Context.Request.Headers["X-Forwarded-Proto"];
        if (!String.IsNullOrEmpty(proto) && proto == "https")
        {
            return true;
        }
        return false;
    }

    bool excludeAnonymous()
    {
        //special logic to prevent Anonymous cache
        if (_context.Context.Request.Cookies["mob_login"] != null && _context.Context.Request.Cookies["mob_login"].Value == "true")
        {
            return true;
        }
        return false;
    }


    void zuz_BeginRequest(object o, EventArgs e)
    {
        //_context.Context.Response.Write(domain_id);
        //_context.Context.Response.Flush();
        //_context.Context.Response.End();
        //return;
        if (!MOBisSupported(_context))
        {
            return;
        }
        if (UseFilter())
        {
            _filter = new ResponseFilter(_context.Response.Filter, _context);
            bool exclude_anonymous = excludeAnonymous();
            _filter.exclude_anonymous = exclude_anonymous;
            string url = _context.Context.Request.Url.ToString();

            if (cache_type == "anonymous" && !exclude_anonymous && _context.Context.Request.HttpMethod == "GET" && !(MOB_Is_Secure_Connection(_context) && !cache_ssl) )
            {

                string hash_str = GetMd5Hash(Encoding.UTF8.GetBytes(url));
                bool got_data = MOB_Fetch_Cache(hash_str);
                if (got_data)
                {
                    return;
                }
            }
            _context.Response.Filter = _filter;
        }
    }


    public void Init(HttpApplication context)
    {
        System.Net.ServicePointManager.ServerCertificateValidationCallback += delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) { return true; };
        _context = context;
        context.BeginRequest += new EventHandler(zuz_BeginRequest);
        //context.PostAcquireRequestState += new EventHandler(zuz_BeginRequest);
    }
}