package com.colin.clipbridge;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import org.json.JSONObject;

public final class Config {
    static SharedPreferences prefs(Context c){return c.getSharedPreferences("bridge",Context.MODE_PRIVATE);}
    private static SecretKey storageKey() throws Exception {
        KeyStore s=KeyStore.getInstance("AndroidKeyStore");s.load(null);
        if(!s.containsAlias("clipbridge-config")) {KeyGenerator g=KeyGenerator.getInstance("AES","AndroidKeyStore");g.init(new KeyGenParameterSpec.Builder("clipbridge-config",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());g.generateKey();}
        return (SecretKey)s.getKey("clipbridge-config",null);
    }
    static void save(Context c,JSONObject value) throws Exception {
        persist(c,"config",value,true);
        prefs(c).edit().remove("pending").remove("pendingIv").apply();
    }
    static void savePending(Context c,JSONObject value)throws Exception {persist(c,"pending",value,false);}
    static JSONObject pending(Context c)throws Exception {return decode(c,"pending","pendingIv");}
    static void updateEndpoints(Context c,JSONObject value)throws Exception {
        synchronized(SyncState.LOCK){
            JSONObject current=read(c);if(!current.getString("key").equals(value.getString("key")))throw new Exception(c.getString(R.string.pair_invalid));
            current.put("endpoints",value.getJSONArray("endpoints"));
            if(value.has("computerId"))current.put("computerId",value.getString("computerId"));
            if(value.has("vapid")){current.put("vapid",value.getString("vapid"));current.remove("firebase");}
            persist(c,"config",current,false);
        }
    }
    static void saveSubscription(Context c,JSONObject value)throws Exception {persist(c,"subscription",value,false);}
    static JSONObject subscription(Context c)throws Exception{return decode(c,"subscription","subscriptionIv");}
    private static void persist(Context c,String prefix,JSONObject value,boolean reset) throws Exception {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,storageKey());
        String body=Base64.encodeToString(cipher.doFinal(value.toString().getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP),iv=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP);
        SharedPreferences.Editor edit=prefs(c).edit().putString(prefix,body).putString(prefix.equals("config")?"iv":prefix+"Iv",iv);
        if(reset)edit.remove("lastSequence").remove("latestScheduledSequence");
        if(!edit.commit())throw new Exception(c.getString(R.string.error_save_config));
    }
    static JSONObject read(Context c) throws Exception {
        return decode(c,"config","iv");
    }
    private static JSONObject decode(Context c,String name,String ivName)throws Exception {
        SharedPreferences p=prefs(c);if(!p.contains(name))throw new Exception(c.getString(R.string.status_unpaired));
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE,storageKey(),new GCMParameterSpec(128,Base64.decode(p.getString(ivName,""),Base64.NO_WRAP)));
        return new JSONObject(new String(cipher.doFinal(Base64.decode(p.getString(name,""),Base64.NO_WRAP)),StandardCharsets.UTF_8));
    }
    static void status(Context c,int resource,Object... args){
        org.json.JSONArray values=new org.json.JSONArray();for(Object arg:args)values.put(String.valueOf(arg));
        prefs(c).edit().putString("statusKey",c.getResources().getResourceEntryName(resource)).putString("statusArgs",values.toString()).apply();
    }
    static String statusText(Context c){
        if(SyncState.paused(c))return c.getString(R.string.sync_paused);
        if(prefs(c).contains("config")&&!SyncState.ready(c)&&!"registration_failed".equals(prefs(c).getString("statusKey","")))return c.getString(R.string.sync_restoring);
        try {
            String name=prefs(c).getString("statusKey","");int id=c.getResources().getIdentifier(name,"string",c.getPackageName());
            if(id==0)return c.getString(prefs(c).contains("config")?R.string.status_paired:R.string.status_unpaired);
            org.json.JSONArray values=new org.json.JSONArray(prefs(c).getString("statusArgs","[]"));Object[] args=new Object[values.length()];for(int i=0;i<args.length;i++)args[i]=values.getString(i);return c.getString(id,args);
        }catch(Exception e){return c.getString(R.string.status_unpaired);}
    }
}
