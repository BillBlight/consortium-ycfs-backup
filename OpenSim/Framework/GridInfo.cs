/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using log4net;
using Nini.Config;

namespace OpenSim.Framework
{
    public struct OSHostURL:IComparable<OSHostURL>, IEquatable<OSHostURL>
    {
        public string Host;
        public int Port;
        public string URL;
        public UriHostNameType URLType;
        public bool SecureHTTP;
        public IPAddress IP;

        public OSHostURL(string url, bool withDNSResolve = false)
        {
            Host = string.Empty;
            Port = 80;
            URL = string.Empty;
            URLType = UriHostNameType.Unknown;
            IP = null;
            SecureHTTP = false;

            if (String.IsNullOrEmpty(url))
                return;

            url = url.ToLowerInvariant();

            try
            {
                int urllen = url.Length;
                if (url[urllen - 1] == '/')
                    --urllen;
                int start;
                if(url.StartsWith("http"))
                {
                    if(url[4] == 's')
                    {
                        start =  8;
                        SecureHTTP = true;
                    }
                    else
                        start = 7;
                }
                else
                    start = 0;

                string host;
                UriHostNameType type;
                int indx = url.IndexOf(':', start, urllen - start);
                if (indx > 0)
                {
                    host = url.Substring(start, indx - start);
                    type = Uri.CheckHostName(host);
                    if (type == UriHostNameType.Unknown || type == UriHostNameType.Basic)
                        return;
                    ++indx;
                    string sport = url.Substring(indx, urllen - indx);
                    int tmp;
                    if (!int.TryParse(sport, out tmp) || tmp < 0 || tmp > 65535)
                        return;
                    URLType = type;
                    Host = host;
                    Port = tmp;
                    URL = (SecureHTTP ? "https://" : "http://") + Host + ":" + Port.ToString() + "/";
                }
                else
                {
                    host = url.Substring(start, urllen - start);
                    type = Uri.CheckHostName(host);
                    if (type == UriHostNameType.Unknown || type == UriHostNameType.Basic)
                        return;
                    URLType = type;
                    Host = host;
                    if (SecureHTTP)
                        URL = "https://" + Host + ":443/";
                    else
                        URL = "http://" + Host + ":80/";
                }

                if (withDNSResolve && URLType == UriHostNameType.Dns)
                {
                    IPAddress ip = Util.GetHostFromDNS(host);
                    if (ip != null)
                        IP = ip;
                }
            }
            catch
            {
                URLType = UriHostNameType.Unknown;
                IP = null;
            }
        }

        public bool ResolveDNS()
        {
            if (URLType == UriHostNameType.Unknown || String.IsNullOrWhiteSpace(Host))
                return false;

            IPAddress ip = Util.GetHostFromDNS(Host);
            if (ip == null)
                return false;
            return true;
        }

        public bool IsValidHost()
        {
            return URLType != UriHostNameType.Unknown;
        }

        public bool IsResolvedHost()
        {
            return (URLType != UriHostNameType.Unknown) && (IP != null);
        }

        public int CompareTo(OSHostURL other)
        {
            if (Port == other.Port && other.URLType != UriHostNameType.Unknown)
            {
                if (URLType == other.URLType)
                    return Host.CompareTo(other.Host);
            }
            return -1;
        }

        public bool Equals(OSHostURL other)
        {
            if (Port == other.Port && other.URLType != UriHostNameType.Unknown)
            {
                if (URLType == other.URLType)
                    return Host.Equals(other.Host);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return URL.GetHashCode();
        }
    }

    public class GridInfo
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private OSHostURL m_gateKeeperURL;
        private HashSet<OSHostURL> m_gateKeeperAlias;

        //private OSHostURL m_homeURL;
        //private HashSet<OSHostURL> m_homeURLAlias;

        public GridInfo (IConfigSource config, string defaultHost = "")
        {
            string[] sections = new string[] { "Startup", "Hypergrid", "UserAgentService" };

            string gatekeeper = Util.GetConfigVarFromSections<string>(config, "GatekeeperURI", sections, String.Empty);
            if (string.IsNullOrEmpty(gatekeeper))
            {
                IConfig serverConfig = config.Configs["GatekeeperService"];
                if (serverConfig != null)
                    gatekeeper = serverConfig.GetString("ExternalName", string.Empty);
            }
            if (string.IsNullOrEmpty(gatekeeper))
            {
                if(!string.IsNullOrEmpty(defaultHost))
                    m_gateKeeperURL = new OSHostURL(defaultHost, true);
            }
            else
                m_gateKeeperURL = new OSHostURL(gatekeeper, true);

            if(m_gateKeeperURL.URLType == UriHostNameType.Unknown)
                throw new Exception(String.Format("could not find gatekeeper URL"));
            if (m_gateKeeperURL.IP == null)
                throw new Exception(String.Format("could not resolve gatekeeper hostname"));

            string gatekeeperURIAlias = Util.GetConfigVarFromSections<string>(config, "GatekeeperURIAlias", sections, String.Empty);

            if (!string.IsNullOrWhiteSpace(gatekeeperURIAlias))
            {
                string[] alias = gatekeeperURIAlias.Split(',');
                for (int i = 0; i < alias.Length; ++i)
                {
                    OSHostURL tmp = new OSHostURL(alias[i].Trim(), false);
                    if(m_gateKeeperAlias == null)
                        m_gateKeeperAlias = new HashSet<OSHostURL>();
                    if (tmp.URLType != UriHostNameType.Unknown)
                        m_gateKeeperAlias.Add(tmp);
                }
            }
        }

        public string GateKeeperURL
        {
            get
            {
                if(m_gateKeeperURL.URLType != UriHostNameType.Unknown)
                    return m_gateKeeperURL.URL;
                return null;
            }
        }

        public int IsLocalGrid(string gatekeeper)
        {
            OSHostURL tmp = new OSHostURL(gatekeeper, false);
            if(tmp.URLType == UriHostNameType.Unknown)
                return -1;
            if (tmp.Equals(m_gateKeeperURL))
                return 1;
            if (m_gateKeeperAlias != null && m_gateKeeperAlias.Contains(tmp))
                return 1;
            return 0;
        }
    }
}