package com.colin.clipbridge;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/** Bounded HTTP transport. A server rejection is distinct from an unreachable computer. */
final class HttpTransport {
    static final class HttpFailure extends IOException {
        final int status;
        HttpFailure(int status){super("HTTP "+status);this.status=status;}
    }
    static final class ResponseTooLarge extends IOException {}
    static final class InvalidAddress extends IOException {}

    static String request(String[] endpoints,String path,String body,int limit,int readTimeout)throws Exception{
        Exception last=new InvalidAddress(),responseFailure=null;
        for(String endpoint:endpoints){
            HttpURLConnection connection=null;
            try{
                URI base;
                try{base=URI.create(endpoint);}catch(IllegalArgumentException e){throw new InvalidAddress();}
                if(!("http".equals(base.getScheme())||"https".equals(base.getScheme()))||base.getHost()==null||base.getRawUserInfo()!=null)throw new InvalidAddress();
                URL url=new URL(base.toString().replaceAll("/$","")+path);
                connection=(HttpURLConnection)url.openConnection();
                connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(4000);connection.setReadTimeout(readTimeout);
                if(body!=null){
                    byte[] encoded=body.getBytes(StandardCharsets.UTF_8);
                    connection.setRequestMethod("POST");connection.setDoOutput(true);connection.setFixedLengthStreamingMode(encoded.length);
                    connection.setRequestProperty("Content-Type","application/json");
                    try(java.io.OutputStream out=connection.getOutputStream()){out.write(encoded);}
                }
                int code=connection.getResponseCode();if(code!=200)throw new HttpFailure(code);
                try(InputStream in=connection.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
                    byte[] buffer=new byte[8192];int n;
                    while((n=in.read(buffer))!=-1){if(n>limit-out.size())throw new ResponseTooLarge();out.write(buffer,0,n);}
                    return out.toString(StandardCharsets.UTF_8.name());
                }
            }catch(HttpFailure|ResponseTooLarge e){last=e;responseFailure=e;}
            catch(Exception e){last=e;}
            finally{if(connection!=null)connection.disconnect();}
        }
        // A stale address tried afterwards must not mask a response from a reachable server.
        throw responseFailure!=null?responseFailure:last;
    }
}
