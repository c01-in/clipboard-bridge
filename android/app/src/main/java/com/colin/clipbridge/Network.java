package com.colin.clipbridge;
import java.nio.charset.StandardCharsets;
import org.json.JSONArray;
import org.json.JSONObject;

final class Network {
    static String request(JSONObject config,String path,String body,int limit) throws Exception {
        return request(config,path,body,limit,7000);
    }
    static String request(JSONObject config,String path,String body,int limit,int readTimeout) throws Exception {
        JSONArray addresses=config.getJSONArray("endpoints");
        if(addresses.length()==0)throw new Exception(LocalText.get(R.string.error_no_address));
        String[] endpoints=new String[addresses.length()];for(int i=0;i<endpoints.length;i++)endpoints[i]=addresses.getString(i);
        try{return HttpTransport.request(endpoints,path,body,limit,readTimeout);}
        catch(HttpTransport.HttpFailure e){
            throw new Exception(LocalText.get(e.status==400&&"/api/files".equals(path)?R.string.error_file_request:R.string.error_http,e.status),e);
        }
        catch(HttpTransport.ResponseTooLarge e){throw new Exception(LocalText.get(R.string.error_response_size),e);}
        catch(HttpTransport.InvalidAddress e){throw new Exception(LocalText.get(R.string.error_address),e);}
        catch(Exception e){throw new Exception(LocalText.get(R.string.error_connect,e.getMessage()),e);}
    }
    static String encryptedBody(JSONObject config,JSONObject value)throws Exception{return new JSONObject().put("data",Crypto.seal(value.toString().getBytes(StandardCharsets.UTF_8),config.getString("key"))).toString();}
    static void register(JSONObject config,JSONObject device)throws Exception {
        byte[] random=new byte[32];new java.security.SecureRandom().nextBytes(random);
        String challenge=java.util.Base64.getEncoder().encodeToString(random);device.put("challenge",challenge);
        JSONObject box=new JSONObject(request(config,"/api/register",encryptedBody(config,device),2048));
        JSONObject ack=new JSONObject(new String(Crypto.open(box.getString("data"),config.getString("key")),StandardCharsets.UTF_8));
        if(!ack.optBoolean("registered")||!challenge.equals(ack.optString("challenge"))||!device.getString("receiveGeneration").equals(ack.optString("receiveGeneration"))||
            device.getLong("revision")!=ack.optLong("revision")||device.getBoolean("paused")!=ack.optBoolean("paused"))throw new Exception(LocalText.get(R.string.pair_invalid));
    }
    static JSONObject pushConfiguration(JSONObject config)throws Exception {
        byte[] random=new byte[32];new java.security.SecureRandom().nextBytes(random);String challenge=java.util.Base64.getEncoder().encodeToString(random);
        JSONObject response=new JSONObject(request(config,"/api/push-configuration",encryptedBody(config,new JSONObject().put("challenge",challenge)),2048));
        JSONObject data=new JSONObject(new String(Crypto.open(response.getString("data"),config.getString("key")),StandardCharsets.UTF_8));
        if(!challenge.equals(data.optString("challenge"))||!data.optString("vapid").matches("[A-Za-z0-9_-]{87}"))throw new Exception(LocalText.get(R.string.pair_invalid));
        String known=config.optString("computerId","");if(!known.isEmpty()&&!known.equals(data.getString("computerId")))throw new Exception(LocalText.get(R.string.pair_invalid));
        return data;
    }
}
