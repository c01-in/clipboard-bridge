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
        JSONArray endpoints=config.getJSONArray("endpoints");Exception last=new Exception("电脑地址为空");
        for(int i=0;i<endpoints.length();i++){
            HttpURLConnection connection=null;
            try {
                URI base=URI.create(endpoints.getString(i));
                if(!("http".equals(base.getScheme())||"https".equals(base.getScheme()))||base.getHost()==null||base.getRawUserInfo()!=null)throw new Exception("电脑地址无效");
                URL url=new URL(base.toString().replaceAll("/$","")+path);
                connection=(HttpURLConnection)url.openConnection();connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(4000);connection.setReadTimeout(7000);
                if(body!=null){connection.setRequestMethod("POST");connection.setDoOutput(true);connection.setRequestProperty("Content-Type","application/json");connection.getOutputStream().write(body.getBytes(StandardCharsets.UTF_8));}
                int code=connection.getResponseCode();if(code!=200)throw new Exception("电脑返回 HTTP "+code);
                try(InputStream in=connection.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
                    byte[] buffer=new byte[8192];int n;while((n=in.read(buffer))!=-1){if(out.size()+n>limit)throw new Exception("接收内容超过大小限制");out.write(buffer,0,n);}return out.toString(StandardCharsets.UTF_8.name());
                }
            } catch(Exception e){last=e;} finally{if(connection!=null)connection.disconnect();}
        }
        throw new Exception("无法连接电脑（请检查局域网／Tailscale和防火墙）",last);
    }
    static String encryptedBody(JSONObject config,JSONObject value)throws Exception{return new JSONObject().put("data",Crypto.seal(value.toString().getBytes(StandardCharsets.UTF_8),config.getString("key"))).toString();}
}
