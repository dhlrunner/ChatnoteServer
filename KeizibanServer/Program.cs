using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KeizibanServer
{
    class Program
    {
        static int basePort = 9000; //ポート番号
        static HttpListener listener = new HttpListener(); //HTTPサーバ
        static Thread thread = new Thread(new ParameterizedThreadStart(WorkerThread));
        static JArray mainDB = new JArray();
        static int messageNum = 0;
        private static void WorkerThread(object arg)
        {
            try
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx = listener.GetContext();
                    ProcessRequest(ctx);
                }
            }
            catch (ThreadAbortException)
            {
                //frm.textBox1.AppendText("Normal Stopping Service");
            }
        }
        private static JObject getRawDetaildb(int index)
        {
            foreach(JObject a in mainDB)
            {
                if (index == Convert.ToInt32(a["index"]))
                {
                    return a;
                }
            }
            return null;
        }
        private static JObject convertDB(JObject a)
        {
            JObject o = new JObject();
            o.Add("index", a["index"].ToString());
            o.Add("timestamp", a["timestamp"].ToString());
            o.Add("title", a["title"].ToString());
            if (a["author"].ToString() == string.Empty)
            {
                byte[] ipaddr = IPAddress.Parse(a["ipaddr"].ToString()).GetAddressBytes();
                o.Add("author", string.Format("匿名({0}.{1})", ipaddr[0], ipaddr[1]));
            }
            else
            {
                o.Add("author", a["author"].ToString());
            }
            o.Add("message", a["message"]);
            o.Add("likes", a["likes"].Count());
            JArray c = new JArray();
            foreach (JObject k in a["comment"])
            {             
                JObject com = new JObject();
                if (k["author"].ToString() == string.Empty)
                {
                    byte[] ipaddr = IPAddress.Parse(k["ipaddr"].ToString()).GetAddressBytes();
                    com.Add("author", string.Format("匿名({0}.{1})", ipaddr[0], ipaddr[1]));
                }
                else
                {
                    com.Add("author", k["author"]);
                }
                
                com.Add("timestamp", k["timestamp"]);
                com.Add("message",k["message"]);
                c.Add(com);
            }
            o.Add("comment", c);
            return o;
        }
        private static void ProcessRequest(HttpListenerContext ctx) //サーバに要求があったとき実行される関数
        {
            string uri = ctx.Request.RawUrl;
            Console.WriteLine("Request: "+uri+" From "+ctx.Request.RemoteEndPoint.Address.ToString());
            if (ctx.Request.Url.LocalPath.Contains("/getMainDB"))
            {
                try
                {
                    JArray newdb = new JArray();
                    foreach(var a in mainDB)
                    {
                        JObject o = new JObject(convertDB((JObject)a));
                        o.Add("commentCount", o["comment"].Count());
                        o["comment"].Parent.Remove();
                        newdb.Add(o);
                    }
                    ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes(newdb.ToString()));
                }
                catch (Exception ex)
                {
                    ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes(ex.ToString()));
                }
            }
            else if (ctx.Request.Url.LocalPath.Contains("/writeMessage"))
            {
                byte[] Client_Req_data = ReadFully(ctx.Request.InputStream);
                addDB(JObject.Parse(Encoding.UTF8.GetString(Client_Req_data)), ctx.Request.RemoteEndPoint.Address);
                ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes("OK"));
            }
            else if (ctx.Request.Url.LocalPath.Contains("/is_likeable"))
            {
                bool ret = true;
                byte[] Client_Req_data = ReadFully(ctx.Request.InputStream);
                JObject clientreqjson = JObject.Parse(Encoding.UTF8.GetString(Client_Req_data));
                JObject detail = getRawDetaildb(Convert.ToInt32(clientreqjson["index"]));
                foreach (JObject a in detail["likes"])
                {
                    if (a["ipaddr"].ToString() == ctx.Request.RemoteEndPoint.Address.ToString())
                    {
                        ret = false;
                    }
                }
                ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes(ret.ToString()));
            }
            else if (ctx.Request.Url.LocalPath.Contains("/getDetail"))
            {
                byte[] reqdata = ReadFully(ctx.Request.InputStream);
                JObject obj = JObject.Parse(Encoding.UTF8.GetString(reqdata));
                ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes(getDetailmsg(Convert.ToInt32(obj["index"])).ToString()));
            }
            else if (ctx.Request.Url.LocalPath.Contains("/writeComment"))
            {
                JObject reqdata = JObject.Parse(Encoding.UTF8.GetString(ReadFully(ctx.Request.InputStream)));
                JObject s = new JObject();
                s.Add("author", reqdata["author"]);
                s.Add("timestamp", DateTimeOffset.Now.ToUnixTimeSeconds());
                s.Add("message", reqdata["message"]);
                s.Add("ipaddr", ctx.Request.RemoteEndPoint.Address.ToString());

                for (int i = 0; i < mainDB.Count(); i++)
                {
                    if (Convert.ToInt32(reqdata["index"]) == Convert.ToInt32(mainDB[i]["index"]))
                    {                       
                        JArray comm = (JArray)mainDB[i]["comment"];
                        comm.Add(s);
                        ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes("OK"));
                        saveDB();
                        return;
                    } 
                }
                    ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes("Error"));
                
            }
            else if (ctx.Request.Url.LocalPath.Contains("/setLikes"))
            {
                JObject reqdata = JObject.Parse(Encoding.UTF8.GetString(ReadFully(ctx.Request.InputStream)));
                try
                {
                    JObject o = new JObject();
                    o.Add("author",reqdata["author"]);
                    o.Add("ipaddr", ctx.Request.RemoteEndPoint.Address.ToString());
                    for(int i = 0; i<mainDB.Count(); i++)
                    {
                        if(Convert.ToInt32(reqdata["index"]) == Convert.ToInt32(mainDB[i]["index"]))
                        {
                            JArray comm = (JArray)mainDB[i]["likes"];
                            comm.Add(o);
                        }
                    }
                    saveDB();
                    ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes("OK"));
                }
                catch(Exception ex)
                {
                    Debug.WriteLine(ex);
                    ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes("Error"));
                }
            }
            else
            {
                ResponceProcessBinary(ctx, Encoding.UTF8.GetBytes("Error"));
            }
        }
        public static byte[] ReadFully(Stream input)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }
        private static JObject getDetailmsg(int index)
        {
            JObject ret = new JObject();
            foreach(var msg in mainDB)
            {
                if(Convert.ToInt32(msg["index"]) == index)
                {
                    ret = convertDB((JObject)msg);
                    //ret["likes"].Remove();
                    return ret;
                }
            }
            return null;
        }
        private static void ResponceProcessBinary(HttpListenerContext ctx, byte[] data)
        {
            //HttpListenerRequest request = ctx.Request;
            HttpListenerResponse response = ctx.Response;
            //헤더 설정        
            response.Headers.Add("Accept-Encoding", "none"); //gzip 처리하기 귀찮으므로 비압축
            response.Headers.Add("Content-Type", "text/html; charset=UTF-8");
            response.Headers.Add("Server", "Ohkawalab_ChatNote_Server");
            //스트림 쓰기
            response.ContentLength64 = data.Length;
            Stream output = response.OutputStream;
            output.Write(data, 0, data.Length);
        }
        static void Main(string[] args)
        {
            if (File.Exists("maindb.json"))
            {
                mainDB = JArray.Parse(File.ReadAllText("maindb.json"));
                messageNum = mainDB.Count;
                Console.WriteLine("ReadDB Success");
            }
            else
            {
                File.WriteAllText("maindb.json", "{ }");
            }
            listener.Prefixes.Add(string.Format("https://*:{0}/", basePort));
            listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
            listener.Start();  //新しいスレッドでサーバ開始
            if (!thread.IsAlive)
                thread.Start(listener);
            Console.WriteLine("Server Started at {0}", listener.Prefixes.ToArray()[0]);
        }
        private static void addDB(JObject d, IPAddress ip)
        {
            JObject add = new JObject();
            long time = DateTimeOffset.Now.ToUnixTimeSeconds();
            add.Add("index", messageNum);
            add.Add("timestamp", time);
            add.Add("title", d["title"].ToString());
            add.Add("author", d["author"].ToString());
            add.Add("ipaddr", ip.ToString());
            add.Add("likes", new JArray());
            add.Add("message", d["message"].ToString());
            add.Add("comment", new JArray());
            mainDB.Add(add);
            messageNum++;
            saveDB();
        }
        private static void saveDB()
        {
            File.WriteAllText("maindb.json", mainDB.ToString());
        }
    }
}
