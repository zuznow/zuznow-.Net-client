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

public class ResponseFilter : MemoryStream, IRequiresSessionState
{

    string[] servers = { "" };
    string api_key = "";
    string domain_id = "";
    string cache_type = "none";
    string cache_ttl = "30";
    bool cache_ssl = false;
    string charset = "";
    string website_domain = "";
    bool compressInput = true;
	bool compressOutput = false;
	
	bool is_ajax = false;
    bool checkContentCache = true;
	


    public bool exclude_anonymous = false;

    private readonly Stream _outputStream;
    private MemoryStream _cachedStream = new MemoryStream(4096);
    private HttpApplication _httpApp;

    private volatile bool _isClosing;
    private volatile bool _isClosed;

    string mob_info;

    public T[] Shuffle<T>(T[] array)
    {
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


    public ResponseFilter(Stream outputStream, HttpApplication context)
    {
        _outputStream = outputStream;
        _httpApp = context;
        _httpApp.Context.Items["MOB_BeginRequest_Time"] = DateTime.Now;
    }

    public string MOB_Get_UA()
    {
        
		
		string url = _httpApp.Context.Request.Url.ToString();
        Uri parsed_url = new Uri(url);
        if (parsed_url.Query != "")
        {
            NameValueCollection parsed_query = HttpUtility.ParseQueryString(parsed_url.Query);
            if (!String.IsNullOrEmpty(parsed_query["mob_ua"]))
            {
                HttpCookie newCookie = new HttpCookie("mob_ua");
                newCookie.Value = parsed_query["mob_ua"];
                _httpApp.Context.Response.Cookies.Add(newCookie);
                _httpApp.Context.Request.Cookies["mobtest"].Value = "true";
                return parsed_query["mob_ua"];
            }
        }
		if (_httpApp.Context.Request.Cookies["mob_ua"] != null)
        {
			return _httpApp.Context.Request.Cookies["mob_ua"].Value;
        }
        return _httpApp.Context.Request.Headers["User-Agent"];
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
            _httpApp.Context.Response.AppendHeader("X-Zuznow-Backend-Time", ((DateTime.Now.Subtract(beginRequestTime).TotalMilliseconds) / 1000.0).ToString());
			



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
            string website_url = _httpApp.Context.Request.Url.ToString().Replace(_httpApp.Context.Request.Url.Host, website_domain);
            mob_info +="<!-- url:"+ website_url + "-->";
            reqparm.Add("url", website_url);
            if (website_url != _httpApp.Context.Request.Url.ToString())
            {
                reqparm.Add("new_url", _httpApp.Context.Request.Url.ToString());
            }
            if (MOBModule.MOBisTest(_httpApp))
            {
                reqparm.Add("force", "true");
            }
            reqparm.Add("user_agent", MOB_Get_UA());
			mob_info += "\n<!-- \n user_agent  " + MOB_Get_UA() + "\n -->";
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
            if (MOB_need_cache())
            {
                string cache_key;
                cache_key = MOB_cache_sting();
                if (cache_key != "")
                {
                    reqparm.Add("cache_key", cache_key);
					reqparm.Add("cache_ttl", cache_ttl);
                }
            }

            try
            {
                bool need_mobilize = true;
                if (checkContentCache)
                {
                    string data_string = System.Text.Encoding.Default.GetString(cachedContent);
                    if (data_string.Contains("mob_domain_id")) //data already converted
                    {
                        need_mobilize = false;
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

            string output;
			
			if(compressInput)
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
				mob_info += "\n<!-- \n compressed_string  " + compressed_string.Length + "\n -->";
			}
			else
			{
				string data_string = System.Convert.ToBase64String(cachedContent);
				reqparm.Add("data", data_string);
				mob_info += "\n<!-- \n data_string " + data_string.Length + "\n -->";
			}
			
            
            string postData = "";
            for (int i = 0; i < reqparm.Keys.Count; i++)
            {
                postData += "&" + reqparm.Keys[i] + "=" + HttpUtility.UrlEncode(reqparm[reqparm.Keys[i]]);
            }

            byte[] bytedata = Encoding.UTF8.GetBytes(postData);

            byte[] response = cachedContent;

            string[] servers_urls = Shuffle(servers);

            for (int i = 0; i < servers_urls.Length; i++)
            {
                bool sucess = false;
                string server_url = servers_urls[i];

                response = cachedContent;
                string url = server_url + "mobilize.php";
				mob_info += "\n<!-- \n server_url " + url+ "\n -->";
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
                        string data_url = location + "&key=" + api_key + "&domain_id=" + domain_id + "&cache_ttl=" + cache_ttl + "&user_agent=" + HttpUtility.UrlEncode(MOB_Get_UA()) + "&charset=" + HttpUtility.UrlEncode(charset);
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
                                /*
                                {
                                    httpWebResponse.GetResponseStream().ReadTimeout = 1000;
                                    MemoryStream memoryStream = new MemoryStream(0x10000);
                                    byte[] buffer = new byte[0x1000];
                                    int bytes;
									
                                    File.AppendAllText(@"c:\tmp\file.txt", "==>READ "+buffer.Length+"\n");
                                    while ((bytes = httpWebResponse.GetResponseStream().Read(buffer, 0, buffer.Length)) > 0) 
                                    {
                                        File.AppendAllText(@"c:\tmp\file.txt", "READ " + bytes +"\n");
                                        memoryStream.Write(buffer, 0, bytes);
                                    }
                                    File.AppendAllText(@"c:\tmp\file.txt", "<==READ "+bytes+"\n");
                                    response = memoryStream.ToArray();
                                }*/
                                Stream st = httpGetWebResponse.GetResponseStream();
                                response = GetResponseBytes(st);
                                //StreamReader streamReader = new StreamReader(st,true);
                                //response = Encoding.ASCII.GetBytes(streamReader.ReadToEnd());
                                //response = GetResponseBytes(httpWebResponse.GetResponseStream());
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
							if(count % 4 == 0)
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

            {
                
				byte[] buffer;
				byte[] compressed;
				if(compressOutput)
				{
					_httpApp.Context.Response.AppendHeader("Content-Encoding", "gzip");
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
                /* Do not add to the content, add to log TBD
                if (!is_ajax)
                {
                    _outputStream.Write(Encoding.ASCII.GetBytes(mob_info), 0, (int)mob_info.Length);
                }*/
                _cachedStream.SetLength(0);
                _outputStream.Flush();
            }
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

    public string MOB_Fetch_Anonynous_Cache()
    {
        if (cache_type == "anonymous" && _httpApp.Context.Request.HttpMethod == "GET")
        {
            string url = _httpApp.Context.Request.Url.ToString();
            byte[] hash;
            using (MD5 md5 = MD5.Create())
            {
                hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
            }
            string hash_str = Encoding.UTF8.GetString(hash, 0, hash.Length);
            string str = MOB_Fetch_Cache(hash_str);
            if (str != "")
            {
                DateTime beginRequestTime = (DateTime)_httpApp.Context.Items["MOB_BeginRequest_Time"];
                //Do not add to the content, add to log TBD
                //str += "\n<!--\nServed from Anonymous cache\nTotal time " + (DateTime.Now.Subtract(beginRequestTime).TotalMilliseconds.ToString()) + "\n -->";
                return str;
            }
        }
        return "";
    }

    public bool MOB_Is_Secure_Connection()
    {
        return _httpApp.Context.Request.IsSecureConnection;
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


    public string MOB_cache_sting()
    {

        byte[] hash;
        string hash_str = "";

        if (!exclude_anonymous && cache_type == "anonymous" && _httpApp.Context.Request.HttpMethod == "GET")
        {
            hash_str = GetMd5Hash(Encoding.UTF8.GetBytes(_httpApp.Context.Request.Url.ToString()));
        }
        else if (cache_type != "none")
        {
            hash_str = GetMd5Hash(_cachedStream.ToArray());
        }
        return hash_str;
    }

    public bool MOB_need_cache()
    {
        if (cache_type == "none" || (MOB_Is_Secure_Connection() && !cache_ssl))
        {
            return false;
        }
        return true;
    }


}