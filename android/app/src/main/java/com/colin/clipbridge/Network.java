package com.colin.clipbridge;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import org.json.JSONArray;
import org.json.JSONObject;

final class Network {
    static String request(JSONObject config,String path,String body,int limit) throws Exception {
        return request(config,path,body,limit,7000);
    }
    static String request(JSONObject config,String path,String body,int limit,int readTimeout) throws Exception {
        JSONArray endpoints=config.getJSONArray("endpoints");Exception last=new Exception(LocalText.get(R.string.error_no_address));
        for(int i=0;i<endpoints.length();i++){
            HttpURLConnection connection=null;
            try {
                URI base=URI.create(endpoints.getString(i));
                if(!("http".equals(base.getScheme())||"https".equals(base.getScheme()))||base.getHost()==null||base.getRawUserInfo()!=null)throw new Exception(LocalText.get(R.string.error_address));
                URL url=new URL(base.toString().replaceAll("/$","")+path);
                connection=(HttpURLConnection)url.openConnection();connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(4000);connection.setReadTimeout(readTimeout);
                if(body!=null){byte[] encoded=body.getBytes(StandardCharsets.UTF_8);connection.setRequestMethod("POST");connection.setDoOutput(true);connection.setFixedLengthStreamingMode(encoded.length);connection.setRequestProperty("Content-Type","application/json");try(java.io.OutputStream out=connection.getOutputStream()){out.write(encoded);}}
                int code=connection.getResponseCode();if(code!=200)throw new Exception(LocalText.get(R.string.error_http,code));
                try(InputStream in=connection.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
                    byte[] buffer=new byte[8192];int n;while((n=in.read(buffer))!=-1){if(out.size()+n>limit)throw new Exception(LocalText.get(R.string.error_response_size));out.write(buffer,0,n);}return out.toString(StandardCharsets.UTF_8.name());
                }
            } catch(Exception e){last=e;} finally{if(connection!=null)connection.disconnect();}
        }
        throw new Exception(LocalText.get(R.string.error_connect,last.getMessage()),last);
    }
    static String encryptedBody(JSONObject config,JSONObject value)throws Exception{return new JSONObject().put("data",Crypto.seal(value.toString().getBytes(StandardCharsets.UTF_8),config.getString("key"))).toString();}
    static void register(JSONObject config,JSONObject device)throws Exception {
        byte[] random=new byte[32];new java.security.SecureRandom().nextBytes(random);
        String challenge=java.util.Base64.getEncoder().encodeToString(random);device.put("challenge",challenge);
        JSONObject box=new JSONObject(request(config,"/api/register",encryptedBody(config,device),2048));
        JSONObject ack=new JSONObject(new String(Crypto.open(box.getString("data"),config.getString("key")),StandardCharsets.UTF_8));
        if(!ack.optBoolean("registered")||!challenge.equals(ack.optString("challenge")))throw new Exception(LocalText.get(R.string.pair_invalid));
    }
}
