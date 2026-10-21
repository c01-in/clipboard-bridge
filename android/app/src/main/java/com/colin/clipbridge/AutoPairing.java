package com.colin.clipbridge;

import android.content.Context;
import android.net.*;
import android.os.Build;
import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** Foreground-only discovery. No debug bridge, background listener or persistent receive service. */
final class AutoPairing implements AutoCloseable {
    interface Listener {void status(int resource,Object... args);void found(Computer computer);void paired();}
    static final class Computer {
        final String id,name,key,endpoint,code;final long expires;final android.net.Network network;
        Computer(String id,String name,String key,String endpoint,long expires,android.net.Network network)throws Exception {this.id=id;this.name=name;this.key=key;this.endpoint=endpoint;this.expires=expires;this.network=network;code=PairingIdentity.code(key);}
    }
    final Context context;final Listener listener;final ExecutorService jobs=Executors.newFixedThreadPool(2);
    final boolean newPair;volatile boolean closed,confirming;volatile DatagramSocket socket;
    final Map<String,Computer> seen=new ConcurrentHashMap<>();
    AutoPairing(Context c,boolean newPair,Listener l){context=c.getApplicationContext();this.newPair=newPair;listener=l;}
    void start(){jobs.execute(this::discover);if(Config.prefs(context).contains("pending")){confirming=true;jobs.execute(this::resume);}}
    private android.net.Network wifi() {
        ConnectivityManager manager=context.getSystemService(ConnectivityManager.class);
        for(android.net.Network n:manager.getAllNetworks()){NetworkCapabilities cap=manager.getNetworkCapabilities(n);if(cap!=null&&cap.hasTransport(NetworkCapabilities.TRANSPORT_WIFI))return n;}return null;
    }
    private void discover(){
        try {
            PairingIdentity identity=new PairingIdentity();String clientId=UUID.randomUUID().toString().replace("-","");
            String phoneName=(Build.MANUFACTURER+" "+Build.MODEL);if(phoneName.length()>100)phoneName=phoneName.substring(0,100);
            JSONObject hello=new JSONObject().put("protocol",PairingIdentity.PROTOCOL).put("kind","hello").put("id",clientId).put("name",phoneName).put("nonce",identity.nonce).put("publicKey",identity.publicKey());
            while(!closed) {
                android.net.Network network=wifi();if(network==null){listener.status(R.string.discovery_wifi);pause(1500);continue;}
                LinkProperties links=context.getSystemService(ConnectivityManager.class).getLinkProperties(network);
                if(links==null){pause(600);continue;}
                InetAddress local=null;for(LinkAddress a:links.getLinkAddresses())if(a.getAddress() instanceof Inet4Address){local=a.getAddress();break;}
                if(local==null){listener.status(R.string.discovery_wifi);pause(1500);continue;}
                try(DatagramSocket udp=new DatagramSocket(null)) {
                    socket=udp;udp.setReuseAddress(true);udp.bind(new InetSocketAddress(local,0));network.bindSocket(udp);udp.setBroadcast(true);udp.setSoTimeout(600);
                    Set<InetAddress> broadcasts=new HashSet<>();broadcasts.add(InetAddress.getByName("255.255.255.255"));
                    java.net.NetworkInterface nic=java.net.NetworkInterface.getByInetAddress(local);if(nic!=null)for(InterfaceAddress a:nic.getInterfaceAddresses())if(a.getBroadcast()!=null)broadcasts.add(a.getBroadcast());
                    long next=0;String challenge=null,requestId="";JSONObject configured=null;
                    try{if(!newPair)configured=Config.read(context);}catch(Exception ignored){}
                    while(!closed&&network.equals(wifi())) {
                        if(System.currentTimeMillis()>=next) {
                            JSONObject message=hello;
                            if(configured!=null) {
                                byte[] nonce=new byte[32];new java.security.SecureRandom().nextBytes(nonce);challenge=Base64.getEncoder().encodeToString(nonce);requestId=configured.optString("computerId","");
                                message=new JSONObject().put("protocol",PairingIdentity.PROTOCOL).put("kind","reconnect").put("computerId",requestId).put("nonce",challenge).put("proof",PairingIdentity.proof(configured.getString("key"),"reconnect\n"+requestId+"\n"+challenge));
                            }
                            byte[] bytes=message.toString().getBytes(StandardCharsets.UTF_8);for(InetAddress dest:broadcasts)try{udp.send(new DatagramPacket(bytes,bytes.length,dest,45872));}catch(IOException ignored){}
                            next=System.currentTimeMillis()+2500;
                        }
                        DatagramPacket packet=new DatagramPacket(new byte[1500],1500);
                        try{udp.receive(packet);}catch(SocketTimeoutException e){continue;}
                        if(packet.getLength()>1400||!(packet.getAddress() instanceof Inet4Address))continue;
                        try {
                            JSONObject offer=new JSONObject(new String(packet.getData(),0,packet.getLength(),StandardCharsets.UTF_8));
                            if(!PairingIdentity.PROTOCOL.equals(offer.optString("protocol"))||offer.optInt("port")!=45871)continue;
                            String endpoint="http://"+packet.getAddress().getHostAddress()+":45871";
                            if(configured!=null) {
                                String computerId=offer.getString("computerId");
                                if(!"known".equals(offer.optString("kind"))||!Objects.equals(challenge,offer.optString("nonce"))||!requestId.equals(offer.getString("requestComputerId"))||(!requestId.isEmpty()&&!requestId.equals(computerId))||
                                    !PairingIdentity.verify(configured.getString("key"),"known\n"+requestId+"\n"+computerId+"\n"+challenge+"\n45871",offer.getString("proof")))continue;
                                JSONArray addresses=new JSONArray().put(endpoint);for(int i=0;i<configured.getJSONArray("endpoints").length();i++){String old=configured.getJSONArray("endpoints").getString(i);if(!old.equals(endpoint))addresses.put(old);}
                                if(!computerId.equals(configured.optString("computerId"))||!endpoint.equals(configured.getJSONArray("endpoints").optString(0))){configured.put("computerId",computerId).put("endpoints",addresses);Config.updateEndpoints(context,configured);RegistrationWorker.retry(context);}
                                listener.status(R.string.discovery_connected);continue;
                            }
                            if(!"offer".equals(offer.optString("kind")))continue;
                            String id=offer.getString("id"),name=offer.getString("name");long expires=offer.getLong("expiresAt");
                            seen.values().removeIf(pc->pc.expires<=System.currentTimeMillis());
                            if(id.length()!=32||name.length()>100||expires<=System.currentTimeMillis()||expires>System.currentTimeMillis()+180000||seen.containsKey(id)||seen.size()>=16)continue;
                            String transcript=String.join("\n",PairingIdentity.PROTOCOL,clientId,PairingIdentity.name(phoneName),identity.nonce,identity.publicKey(),PairingIdentity.name(name),id,offer.getString("nonce"),offer.getString("publicKey"));
                            Computer pc=new Computer(id,name,identity.agree(offer.getString("publicKey"),transcript),endpoint,expires,network);seen.put(id,pc);listener.found(pc);
                        }catch(Exception ignored){ /* Never trust discovery metadata as authentication. */ }
                    }
                }catch(IOException e){if(!closed){listener.status(R.string.discovery_retry);pause(1500);}}
            }
        }catch(Exception e){if(!closed)listener.status(R.string.discovery_failed,e.getMessage());}
    }
    void confirm(Computer pc){if(confirming||closed)return;confirming=true;jobs.execute(()->{
        try {
            JSONObject confirmation=new JSONObject().put("kind","phone-confirm").put("sessionId",pc.id);
            request(pc,"/confirm",new JSONObject().put("data",Crypto.seal(confirmation.toString().getBytes(StandardCharsets.UTF_8),pc.key)).toString());
            listener.status(R.string.pair_wait_computer);
            JSONObject configuration=null;
            while(!closed&&System.currentTimeMillis()<pc.expires) {
                try{JSONObject box=new JSONObject(request(pc,"/configuration",null));configuration=new JSONObject(new String(Crypto.open(box.getString("data"),pc.key),StandardCharsets.UTF_8));break;}catch(PairNotReady e){pause(1200);}
            }
            if(configuration==null)throw new IOException(context.getString(R.string.pair_expired));
            if(Base64.getDecoder().decode(configuration.getString("key")).length!=32||configuration.getJSONArray("endpoints").length()==0)throw new IOException(context.getString(R.string.pair_invalid));
            JSONObject pending=new JSONObject().put("configuration",configuration).put("id",pc.id).put("key",pc.key).put("endpoint",pc.endpoint);
            Config.savePending(context,pending);finish(pending,pc.network);
        }catch(Exception e){if(!closed)listener.status(R.string.pair_failed,e.getMessage());}
        finally{confirming=false;}
    });}
    private void resume(){try{JSONObject pending=Config.pending(context);finish(pending,wifi());}catch(Exception e){if(!closed)listener.status(R.string.pair_resume);}finally{confirming=false;}}
    private void finish(JSONObject pending,android.net.Network network)throws Exception {
        JSONObject config=pending.getJSONObject("configuration");listener.status(R.string.pair_registering);
        JSONObject device=SyncState.device(context,PushRegistration.ensure(context,config));
        JSONObject complete=new JSONObject(device.toString()).put("kind","complete").put("sessionId",pending.getString("id"));
        Computer pc=new Computer(pending.getString("id"),"",pending.getString("key"),pending.getString("endpoint"),0,network);
        boolean done=false;
        try {JSONObject response=new JSONObject(request(pc,"/complete",new JSONObject().put("data",Crypto.seal(complete.toString().getBytes(StandardCharsets.UTF_8),pc.key)).toString()));done=new JSONObject(new String(Crypto.open(response.getString("data"),pc.key),StandardCharsets.UTF_8)).optBoolean("completed");}
        catch(Exception lost){ // If Windows committed before a lost reply/restart, possession of the new permanent key recovers it.
            com.colin.clipbridge.Network.register(config,device);done=true;
        }
        if(!done)throw new IOException(context.getString(R.string.pair_invalid));
        Config.save(context,config);SyncState.registered(context,device);Config.status(context,R.string.status_paired);RegistrationWorker.retry(context);listener.paired();
    }
    private static final class PairNotReady extends IOException {}
    private String request(Computer pc,String operation,String body)throws Exception {
        URL url=new URL(pc.endpoint+"/api/pairing/"+pc.id+operation);HttpURLConnection connection=(HttpURLConnection)(pc.network==null?url.openConnection():pc.network.openConnection(url));
        try {
            connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(4000);connection.setReadTimeout(7000);
            if(body!=null){byte[] bytes=body.getBytes(StandardCharsets.UTF_8);connection.setRequestMethod("POST");connection.setDoOutput(true);connection.setRequestProperty("Content-Type","application/json");connection.setFixedLengthStreamingMode(bytes.length);try(OutputStream out=connection.getOutputStream()){out.write(bytes);}}
            int status=connection.getResponseCode();if(status==409)throw new PairNotReady();if(status!=200)throw new IOException("HTTP "+status);
            try(InputStream in=connection.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] bytes=new byte[4096];for(int n;(n=in.read(bytes))!=-1;){if(out.size()+n>32768)throw new IOException("Pairing response too large");out.write(bytes,0,n);}return out.toString(StandardCharsets.UTF_8.name());}
        }finally{connection.disconnect();}
    }
    private void pause(long ms){try{Thread.sleep(ms);}catch(InterruptedException e){Thread.currentThread().interrupt();}}
    public void close(){closed=true;DatagramSocket active=socket;if(active!=null)active.close();jobs.shutdownNow();}
}
