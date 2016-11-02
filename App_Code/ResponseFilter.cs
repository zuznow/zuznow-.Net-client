using System;
using System.IO;
using System.Web;
using System.Web.SessionState;
using System.Text;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Configuration;

public class ResponseFilter : MemoryStream, IRequiresSessionState
{

    string[] servers = MOBModule.GetConf("mob_servers", "").Split(',');
    string api_key = MOBModule.GetConf("mob_api_key", "");
    string domain_id = MOBModule.GetConf("mob_domain_id", "");
    string cache_type = MOBModule.GetConf("mob_cache_type", "");
    string cache_ttl = MOBModule.GetConf("mob_cache_ttl", "");
    string website_domain = MOBModule.GetConf("mob_website_domain", "");
    bool cache_ssl = Boolean.Parse(MOBModule.GetConf("mob_cache_ssl", "false"));
    string charset = MOBModule.GetConf("mob_charset", "");
    
    bool compressInput =  Boolean.Parse(MOBModule.GetConf("mob_compressInput", "true"));
    bool compressOutput = Boolean.Parse(MOBModule.GetConf("mob_compressOutput", "false"));
	
	bool force_test = Boolean.Parse(MOBModule.GetConf("mob_force_test", "false"));

    bool is_ajax = false;
    bool checkContentCache = true;



    public bool exclude_anonymous = false;

    private readonly Stream _outputStream;
    private MemoryStream _cachedStream = new MemoryStream(4096);
    private HttpApplication _httpApp;

    private volatile bool _isClosing;
    private volatile bool _isClosed;

    string mob_info;

    


    public ResponseFilter(Stream outputStream, HttpApplication context)
    {
        _outputStream = outputStream;
        _httpApp = context;
        _httpApp.Context.Items["MOB_BeginRequest_Time"] = DateTime.Now;
    }

   

    private byte[] GetResponseBytes(Stream responseStream)
    {
        MemoryStream memoryStream = new MemoryStream(0x1000000);
        byte[] buffer = new byte[0x100000];
        int bytes;

        while ((bytes = responseStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            memoryStream.Write(buffer, 0, bytes);
        }

        byte[] response = memoryStream.ToArray();
        return response;

    }

    public override void Flush()
    {
        if (_isClosing && !_isClosed)
        {
            DateTime beginRequestTime = (DateTime)_httpApp.Context.Items["MOB_BeginRequest_Time"];
            DateTime filterBeginRequestTime = DateTime.Now;
            try
            {
                _httpApp.Context.Response.AppendHeader("X-Zuznow-Backend-Time", ((DateTime.Now.Subtract(beginRequestTime).TotalMilliseconds) / 1000.0).ToString());
            }
            catch (System.Web.HttpException)
            {
                
            }




            string content_type = _httpApp.Context.Response.ContentType;
            if (_cachedStream.Length == 0 || _httpApp.Context.Response == null || _httpApp.Context.Response.StatusCode != 200 || content_type == "" || (content_type.IndexOf("text/html") == -1 && content_type.IndexOf("text/plain") == -1))
            {
                if (_cachedStream.Length > 0)
                {
                    _outputStream.Write(_cachedStream.ToArray(), 0, (int)_cachedStream.Length);
                    _cachedStream.SetLength(0);
                }
                _outputStream.Flush();
                return;
            }

            if (charset == "")
            {


                int pos = content_type.IndexOf("charset=");
                if (pos != -1)
                {
                    charset = content_type.Substring(pos + 8);
                }

                if (charset == "")
                {
                    charset = "utf-8";
                }
            }
            byte[] cachedContent = _cachedStream.ToArray();

            System.Collections.Specialized.NameValueCollection reqparm = new System.Collections.Specialized.NameValueCollection();
            reqparm.Add("key", api_key);
            reqparm.Add("charset", charset);
            reqparm.Add("domain_id", domain_id);
            string proto = _httpApp.Context.Request.Headers["X-Forwarded-Proto"];
            bool proto_https = false;
            if (!String.IsNullOrEmpty(proto) && proto == "https")
            {
                proto_https = true;
            }
            string new_url = _httpApp.Context.Request.Url.ToString();
            string website_url = new_url.Replace(_httpApp.Context.Request.Url.Host, website_domain);
            if (proto_https && !_httpApp.Context.Request.IsSecureConnection)
            {
                website_url = website_url.Replace("http://", "https://");
                new_url = new_url.Replace("http://", "https://");
            }
            //mob_info += "<!-- url:" + website_url + "-->";
            reqparm.Add("url", website_url);
            if (website_url != new_url)
            {
                reqparm.Add("new_url", new_url);
            }
            if ( force_test || MOBModule.MOBisTest(_httpApp))
            {
                reqparm.Add("force", "true");
            }
            reqparm.Add("user_agent", MOBModule.MOB_Get_UA(_httpApp));
            //mob_info += "\n<!-- \n user_agent  " + MOBModule.MOB_Get_UA(_httpApp) + "\n -->";
            string requested_with = _httpApp.Context.Request.Headers["X-Requested-With"];
            string requested_microsoft_ajax = _httpApp.Context.Request.Headers["X-MicrosoftAjax"];
            if (String.Equals(requested_with, "xmlhttprequest", StringComparison.OrdinalIgnoreCase) || (requested_microsoft_ajax != null && requested_microsoft_ajax.Length > 0))
            {
                reqparm.Add("ajax", "true");
                is_ajax = true;
            }
            else
            {
                reqparm.Add("ajax", "false");
            }
            if (cache_type != "")
            {
                if (MOB_need_cache())
                {
                    string cache_key;
                    cache_key = MOB_cache_sting();
                    reqparm.Add("cache_key", cache_key);
                    if (cache_ttl != "")
                    {
                        reqparm.Add("cache_ttl", cache_ttl);
                    }
                }
                else
                {
                    reqparm.Add("cache_key", "");
                }
            }
           

            try
            {
                if (checkContentCache)
                {
                    string data_string = System.Text.Encoding.Default.GetString(cachedContent);
                    if (data_string.Contains("mob_domain_id")) //data already converted
                    {

                        if (_cachedStream.Length > 0)
                        {
                            _outputStream.Write(_cachedStream.ToArray(), 0, (int)_cachedStream.Length);
                            _cachedStream.SetLength(0);
                        }
                        _outputStream.Flush();
                        return;
                    }
                }
            }
            catch (Exception ex) { }


            if (compressInput)
            {
                byte[] compressed;
                using (MemoryStream outStream = new MemoryStream())
                {
                    using (GZipStream tinyStream = new GZipStream(outStream, CompressionMode.Compress))
                    using (MemoryStream mStream = new MemoryStream(cachedContent))
                        mStream.WriteTo(tinyStream);
                    compressed = outStream.ToArray();
                }

                string compressed_string = System.Convert.ToBase64String(compressed);
                reqparm.Add("data", compressed_string);
                //mob_info += "\n<!-- \n compressed_string  " + compressed_string.Length + "\n -->";
            }
            else
            {
                string data_string = System.Convert.ToBase64String(cachedContent);
                reqparm.Add("data", data_string);
                //mob_info += "\n<!-- \n data_string " + data_string.Length + "\n -->";
            }


            string postData = "";
            for (int i = 0; i < reqparm.Keys.Count; i++)
            {
                postData += "&" + reqparm.Keys[i] + "=" + HttpUtility.UrlEncode(reqparm[reqparm.Keys[i]]);
            }

            byte[] bytedata = Encoding.UTF8.GetBytes(postData);

            byte[] response = cachedContent;

            string[] servers_urls = MOBModule.Shuffle(servers);

            for (int i = 0; i < servers_urls.Length; i++)
            {
                bool sucess = false;
                string server_url = servers_urls[i];

                response = cachedContent;
                string url = server_url + "mobilize.php";
                //mob_info += "\n<!-- \n server_url " + url + "\n -->";
                try
                {
                    HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(url);
                    httpRequest.Method = "POST";
                    httpRequest.ContentType = "application/x-www-form-urlencoded";
                    httpRequest.AllowAutoRedirect = false;

                    httpRequest.ContentLength = bytedata.Length;
                    Stream requestStream = httpRequest.GetRequestStream();
                    requestStream.Write(bytedata, 0, bytedata.Length);
                    requestStream.Close();

                    HttpWebResponse httpWebResponse = (HttpWebResponse)httpRequest.GetResponse();

                    if (httpWebResponse.StatusCode == HttpStatusCode.OK)
                    {
                        response = GetResponseBytes(httpWebResponse.GetResponseStream());

                        mob_info += "\n<!-- \nTotal time " + (((DateTime.Now.Subtract(beginRequestTime).TotalMilliseconds) / 1000.0).ToString()) + "\n -->";
                        mob_info += "\n<!-- \nFilter time " + (((DateTime.Now.Subtract(filterBeginRequestTime).TotalMilliseconds) / 1000.0).ToString()) + "\n -->";
                        sucess = true;
                    }
                    else if (httpWebResponse.StatusCode == HttpStatusCode.Redirect)
                    {
                        string location = httpWebResponse.Headers["Location"];
                        string Lbzuz = httpWebResponse.Headers["X-LBZUZ"];
                        string data_url = location + "&key=" + api_key + "&domain_id=" + domain_id + "&cache_ttl=" + cache_ttl + "&user_agent=" + HttpUtility.UrlEncode(MOBModule.MOB_Get_UA(_httpApp)) + "&charset=" + HttpUtility.UrlEncode(charset);
                        int count = 1;
                        HttpWebResponse httpGetWebResponse = null;
                        while (count <= 120)
                        {
                            //File.AppendAllText(@"c:\tmp\file.txt", "Fetch from cache  " + count + " "+ @data_url+"\n");
                            try
                            {
                                HttpWebRequest httpGetRequest = (HttpWebRequest)WebRequest.Create(data_url);
                                httpGetRequest.Method = "GET";
                                httpGetRequest.AllowAutoRedirect = false;
                                if (!String.IsNullOrEmpty(Lbzuz))
                                {
                                    httpGetRequest.Headers.Add("X-LBZUZ", Lbzuz);
                                }
                                httpGetWebResponse = (HttpWebResponse)httpGetRequest.GetResponse();
                            }
                            catch (WebException ex)
                            {
                                //mob_info+= "\n<!-- \nFailed "+ex+"\n -->";
                            }
                            if (httpGetWebResponse != null && httpGetWebResponse.StatusCode == HttpStatusCode.OK)
                            {
                              
                                Stream st = httpGetWebResponse.GetResponseStream();
                                response = GetResponseBytes(st);
                                mob_info += "\n<!-- \nTotal time " + (((DateTime.Now.Subtract(beginRequestTime).TotalMilliseconds) / 1000.0).ToString()) + "\n -->";
                                mob_info += "\n<!-- \nFilter time " + (((DateTime.Now.Subtract(filterBeginRequestTime).TotalMilliseconds) / 1000.0).ToString()) + "\n -->";




                                sucess = true;
                                break;
                            }
                            else if (httpGetWebResponse != null && httpGetWebResponse.StatusCode != HttpStatusCode.NotFound && httpGetWebResponse.StatusCode != HttpStatusCode.Redirect)
                            {
                                StreamReader streamReader = new StreamReader(httpGetWebResponse.GetResponseStream(), true);
                                //File.AppendAllText(@"c:\tmp\file.txt", "Redirect+NOT OK " + streamReader.ReadToEnd() +"\n");
                                mob_info += "\n<!-- \n" + httpGetWebResponse.StatusCode + "\n" + streamReader.ReadToEnd() + "\n -->";
                                break;
                            }
                            if (count % 4 == 0)
                            {
                                mob_info += "\n<!-- \nResponse " + count + " " + (((DateTime.Now.Subtract(filterBeginRequestTime).TotalMilliseconds) / 1000.0).ToString()) + "\n -->";
                            }
                            System.Threading.Thread.Sleep(250);
                            count++;
                        }
                        if (count >= 120)
                        {
                            if (httpGetWebResponse != null)
                            {
                                StreamReader streamReader = new StreamReader(httpGetWebResponse.GetResponseStream(), true);
                                //response+= Encoding.UTF8.GetBytes("\n<!-- \n"+httpGetWebResponse.StatusCode+"\n"+streamReader.ReadToEnd()+"\n -->");
                            }
                            else
                            {
                                //response+= Encoding.UTF8.GetBytes("\n<!-- \nFailed "+url+"\n -->");
                            }
                        }
                    }
                }
                catch (WebException ex)
                {

                    if (ex.Response is HttpWebResponse)
                    {
                        HttpWebResponse webResponse = ((HttpWebResponse)ex.Response);
                        StreamReader streamReader = new StreamReader(webResponse.GetResponseStream(), true);
                        mob_info += "\n<!-- \n" + webResponse.StatusCode + "\n" + streamReader.ReadToEnd() + "\n -->";
                    }
                    else
                    {
                        //mob_info+="\n<!-- \nFailed "+ex+"\n -->";
                    }
                }
                if (sucess)
                {
                    break;
                }
            }

            

                byte[] buffer;
                if (compressOutput)
                {
                    _httpApp.Context.Response.Headers["Content-Encoding"] ="gzip";
                    using (MemoryStream outStream = new MemoryStream())
                    {
                        using (GZipStream tinyStream = new GZipStream(outStream, CompressionMode.Compress))

                        using (MemoryStream mStream = new MemoryStream())
                        {
                            mStream.Write(response, 0, (int)response.Length);
                            //mStream.Write(Encoding.ASCII.GetBytes(mob_info), 0, (int)mob_info.Length); Do not add to the content, add to log TBD
                            mStream.WriteTo(tinyStream);
                        }
                        buffer = outStream.ToArray();


                    }
                }
                else
                {
                    buffer = response;
                }

                //byte[] buffer = compressed;
                _cachedStream = new MemoryStream();
                _outputStream.Write(buffer, 0, (int)buffer.Length);
                /* Do not add to the content, add to log TBD*/
                if (!is_ajax)
                {
                    _outputStream.Write(Encoding.ASCII.GetBytes(mob_info), 0, (int)mob_info.Length);
                }
                _cachedStream.SetLength(0);
                _outputStream.Flush();
            
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _cachedStream.Write(buffer, offset, count);
    }
    public override void WriteByte(byte value)
    {
        _cachedStream.WriteByte(value);
    }
    public override void Close()
    {
        _isClosing = true;
        Flush();
        _isClosed = true;
        _isClosing = false;
        _outputStream.Close();
    }

    

    public string MOB_cache_sting()
    {

        string hash_str = "";

        if (!exclude_anonymous && cache_type == "anonymous" && _httpApp.Context.Request.HttpMethod == "GET")
        {
            hash_str = MOBModule.GetMd5Hash(Encoding.UTF8.GetBytes(_httpApp.Context.Request.Url.ToString()));
        }
        else if (cache_type == "personalized")
        {
            hash_str = MOBModule.GetMd5Hash(_cachedStream.ToArray());
        }
        return hash_str;
    }

    public bool MOB_need_cache()
    {
        if (cache_type == "none" || (MOBModule.MOB_Is_Secure_Connection(_httpApp) && !cache_ssl))
        {
            return false;
        }
        return true;
    }


}