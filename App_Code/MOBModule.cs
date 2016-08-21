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




public class MOBModule : IHttpModule, IRequiresSessionState
{

    string[] servers = { "" };
    string api_key = "";
    string domain_id = "";
    string cache_type = "none";
    string cache_ttl = "30";



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
    public  bool MOBisSupported(HttpApplication httpApplication)
    {
        
		try{
			if (httpApplication.Request.Cookies["c2m_original"] != null && httpApplication.Request.Cookies["c2m_original"].Value == "true")
			{
				return false;
			}
			
			if (MOBisTest(httpApplication))
			{
				return true;
			}
			//return false;
			string user_agent = MOB_Get_UA();
			if(user_agent == null)
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
		catch (Exception  ex)
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
		if (currentUrl.IndexOf(".css") != -1 || currentUrl.IndexOf(".js") != -1 || currentUrl.IndexOf(".jpg") != -1 || currentUrl.IndexOf(".gif") != -1 || currentUrl.IndexOf(".png") != -1)
		{
			return false;
		}
		return true;
    }
	
    public string MOB_Get_UA()
    {
        
		
		string url = _context.Context.Request.Url.ToString();
        Uri parsed_url = new Uri(url);
        if (parsed_url.Query != "")
        {
            NameValueCollection parsed_query = HttpUtility.ParseQueryString(parsed_url.Query);
            if (!String.IsNullOrEmpty(parsed_query["mob_ua"]))
            {
                HttpCookie newCookie = new HttpCookie("mob_ua");
                newCookie.Value = parsed_query["mob_ua"];
                _context.Context.Response.Cookies.Add(newCookie);
                _context.Context.Request.Cookies["mobtest"].Value = "true";
                return parsed_query["mob_ua"];
            }
        }
		if (_context.Context.Request.Cookies["mob_ua"] != null)
        {
			return _context.Context.Request.Cookies["mob_ua"].Value;
        }
        return _context.Context.Request.Headers["User-Agent"];
    }


    public string MOB_Fetch_Cache(string cache_key)
    {
        string my_url = servers[0] + "get_cached_data.php?key=" + api_key + "&domain_id=" + domain_id + "&cache_key=" + cache_key + "&cache_ttl=" + cache_ttl + "&user_agent=" + HttpUtility.UrlEncode(MOB_Get_UA());

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
            return "";
        }
        if (httpGetWebResponse != null && httpGetWebResponse.StatusCode == HttpStatusCode.OK)
        {
            StreamReader streamReader = new StreamReader(httpGetWebResponse.GetResponseStream(), true);
            return streamReader.ReadToEnd();
        }
        return "";
    }

    public string GetMd5Hash(byte[] input)
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

    public bool MOB_Is_Secure_Connection()
    {
        return _context.Context.Request.IsSecureConnection;
    }

    bool excludeAnonymous()
    {
        //spacial logic to prevent Anonymous cache
		if (_context.Context.Request.Cookies["mob_login"] != null && _context.Context.Request.Cookies["mob_login"].Value == "true")
        {
            return true;
        }
		return false;
    }
   

    void zuz_BeginRequest(object o, EventArgs e)
    {
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
			bool waitForResponce =  true;

            if (cache_type == "anonymous" && !exclude_anonymous && !waitForResponce && _context.Context.Request.HttpMethod == "GET" && !MOB_Is_Secure_Connection())
            {
               
                string hash_str = GetMd5Hash(Encoding.UTF8.GetBytes(url));
                string data = MOB_Fetch_Cache(hash_str);
                if (data != "")
                {
                    _context.Context.Response.Write(data + "<!--from anonymous cache -->");
                    _context.Context.Response.Flush();
                    _context.Context.Response.End();
                    return;
                }
            }
            _context.Response.Filter = _filter;
        }
    }
    private static Regex regex = new Regex(@"(?<=<title>)[\w\s\r\n]*?(?=</title)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private ResponseFilter _filter;
    private HttpApplication _context;

    public void Init(HttpApplication context)
    {
        System.Net.ServicePointManager.ServerCertificateValidationCallback += delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) { return true; };
        _context = context;
        context.BeginRequest += new EventHandler(zuz_BeginRequest);
        //context.PostAcquireRequestState += new EventHandler(zuz_BeginRequest);
    }
}