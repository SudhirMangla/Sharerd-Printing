using log4net;
using log4net.Config;
using System.Net;
using System.Net.Sockets;
using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace general {
public class Socket<Ttransfertype> : AProtocol<Ttransfertype> {
	private bool ttl;
	private int timeout;
	private Socket server;
	private Dictionary<string,Socket> sockets;
	private int tickcountstart;
	private IPHostEntry hostentry;
	readonly private ILog log = LogManager.GetLogger (typeof(Socket<Ttransfertype>));

	public Socket(Ddecapsulate d, Dencapsulate e, Dtopomsg c) : base(d,e,c) {
	this.ttl = true;
	this.sockets = new Dictionary<string,Socket> ();
	this.timeout = 300;
	log.Debug ("Created Socket Protocol.");
	}

	override public void connect (string url, int port) {
	log.Info ("Connecting to: " + url + " on port: " + port);
	this.hostentry = Dns.GetHostEntry(Dns.GetHostName());
	try {
		foreach(IPAddress address in this.hostentry.AddressList){
		IPEndPoint ipe = new IPEndPoint(address, port);
		this.sockets.Add(url,new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp));
		this.sockets[url].Connect (ipe);
			if(this.sockets[url].Connected){
			log.Debug("Protocol connected, asking topology to handshake.");
			this.topomsg(null,url,new ATopology<Ttransfertype>.Dsend(this.sendmsg),new ATopology<Ttransfertype>.Drecv(this.recv));
			break;
			} else {
			continue;
			}
		}
	} catch (Exception e){
	log.Error ("Cannot Connect: " + e.ToString());
	}
	}

	override public void listen (string url, int port) {
	log.Info ("Listing on port: " + port.ToString() + " with url: " + url);
	IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
	IPAddress ipAddress = ipHostInfo.AddressList[0];
	IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);
	this.server = new Socket(AddressFamily.InterNetwork,SocketType.Stream, ProtocolType.Tcp);
	int id = 0;
	try{
	server.Bind(localEndPoint);
	server.Listen(10);
		while(ttl || id < 10){
		++id;
		log.Debug ("Waiting for network activity.");
		this.sockets.Add(url+id.ToString(),server.Accept());
		log.Debug("Waiting for: " + url+id.ToString() + " ,connected party to make request.");
		this.recv(url+id.ToString());
		}
	} catch (Exception e){
	log.Error ("Listen failed: " + e.ToString());
	}
	}

	override public void disconnect () {
	log.Info ("Stopping to listen.");
	this.ttl = false;
	}

	override public void sendmsg (AComputer<Ttransfertype> obj) {
	log.Debug ("Sending on socket id: " + obj.to);
	this.tickcountstart = Environment.TickCount;
	IFormatter formatter = new BinaryFormatter();
	Stream stream = new MemoryStream();
	formatter.Serialize(stream, obj);
	stream.Close ();
	byte[] buffsol = System.Text.Encoding.Unicode.GetBytes ("<SOL>");
	byte[] buff = ((MemoryStream)stream).ToArray();
	byte[] buffeol = System.Text.Encoding.Unicode.GetBytes ("<EOL>");
	byte[] buffer  = new byte[buff.Length + buffeol.Length + buffsol.Length];
	buffsol.CopyTo (buffer,0);
	buff.CopyTo (buffer, buffsol.Length);
	buffeol.CopyTo (buffer, buff.Length+buffsol.Length);

	int offset = 0;
	int size = buffer.Length-1;
	int sent = 0;
	log.Debug("Sending bytes: " + size);
		do {
			if (Environment.TickCount > this.tickcountstart + this.timeout){
			log.Error("Socket Timed out on sedning.");
			break;
			}
			try {
			sent += this.sockets[obj.to].Send(buffer, offset + sent, size - sent, SocketFlags.None);
			log.Debug("Just sent: " + sent.ToString());
			} catch (SocketException ex){
				if (ex.SocketErrorCode == SocketError.WouldBlock || ex.SocketErrorCode == SocketError.IOPending || ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable){
				Thread.Sleep(30);
				} else {
				log.Error("Could not send on socket Exception: " + ex.ToString());
				}
			}
		} while (sent < size);
	}

	override public void send (ref Ttransfertype obj, string id){
	log.Debug ("encapsulating data to send to: " + id);
	this.encap(ref obj,new ATopology<Ttransfertype>.Dsend(this.sendmsg),id);
	}

	public override void recv(string id){
	log.Debug ("Recving on socket id: " + id);
	bool sol = false, eol = false;
	this.tickcountstart = Environment.TickCount;
	List<byte> buffer = new List<byte> ();
	int rec = 0;
		if (this.sockets [id].Poll (-1, SelectMode.SelectWrite)) {
		do {
			if (Environment.TickCount > this.tickcountstart + this.timeout) {
			log.Error ("Socket Timed out on recving.");
			break;
			}
			try {
			byte[] buff = new byte[255];
			rec += this.sockets [id].Receive (buff,255, SocketFlags.None);
			log.Debug ("Recving Bytes: " + rec);
			buffer.AddRange (buff);
			} catch (SocketException ex) {
				if (ex.SocketErrorCode == SocketError.WouldBlock || ex.SocketErrorCode == SocketError.IOPending || ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable) {
				Thread.Sleep (30);
				} else {
				log.Error ("Could not recive on socket Exception: " + ex.ToString());
				}
			}
			string t = new string(System.Text.Encoding.Unicode.GetChars(buffer.ToArray()));
			//log.Debug("T is: " + t + "\n");
			if (buffer.Count < 1){
			log.Debug("Nothing read on socket.");
			break;
			} else if (sol == false){
			sol = Regex.IsMatch(t,Regex.Escape("<SOL>") + ".");
			this.tickcountstart = Environment.TickCount;
			log.Debug("SOL FOUND.");
			} else {
			eol = Regex.IsMatch(t,"." + Regex.Escape("<EOL>") + ".");
				if (eol == true){
				log.Debug("EOL FOUND.");
				break;
				}
			}
		} while (true);
		} else {
		log.Debug ("Poll returned nothing to read.");
		}
		log.Debug ("Recived " + rec.ToString() + " bytes");
		string te =	new string(System.Text.Encoding.Unicode.GetChars(buffer.GetRange (0, 10).ToArray()));
		log.Debug ("Start is: " + te + ". " + te.Length);
		if (te.Equals ("<SOL>", StringComparison.Ordinal)) {
		log.Debug ("<SOL> is at the start, removing. ");
		buffer.RemoveRange (0,10);
		sol = false;
		}
		te = new string(System.Text.Encoding.Unicode.GetChars(buffer.GetRange (rec-19, 10).ToArray()));
		log.Debug ("End is: " + te + ". " + te.Length);
		if (te.Equals ("<EOL>", StringComparison.Ordinal)) {
		log.Debug ("<EOL> is at the End, removing.");
		buffer.RemoveRange (rec-19, 10);
		eol = false;
		}

		if (sol == false && eol == false) {
		log.Debug ("Deserilizing obj");
		Computer<Ttransfertype> obj;
		IFormatter formatter = new BinaryFormatter ();
		Stream stream = new MemoryStream (buffer.ToArray());
		obj = (Computer<Ttransfertype>)formatter.Deserialize (stream);
		log.Debug("Msg status: " + obj.topologymessage.ToString ());
			if (obj.topologymessage == false) {
			log.Debug ("Data found returning to client.");
			this.decap (obj, id);
			} else {
			log.Debug ("Message found sending to topology.");
			this.topomsg (obj, id, new ATopology<Ttransfertype>.Dsend (this.sendmsg), new ATopology<Ttransfertype>.Drecv (this.recv));
			}
		}
	}
}
}