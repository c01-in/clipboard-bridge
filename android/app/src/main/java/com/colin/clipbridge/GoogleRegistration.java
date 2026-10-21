package com.colin.clipbridge;

import android.content.*;
import android.os.*;
import java.util.concurrent.atomic.AtomicInteger;
import org.unifiedpush.android.embedded_fcm_distributor.impl.FirebaseReceiver;

/** Short-lived Messenger RPC used by Play Services' registration service. No Firebase project is required. */
final class GoogleRegistration implements ServiceConnection {
    private static final AtomicInteger REQUESTS=new AtomicInteger();
    private final Context context;
    private final Intent original;
    private final int requestId=REQUESTS.incrementAndGet();
    private final int googleUid;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final Runnable timeout;
    private boolean bound,finished;
    private GoogleRegistration(Context c,Intent request)throws Exception{
        context=c;original=request;googleUid=c.getPackageManager().getApplicationInfo("com.google.android.gms",0).uid;
        timeout=()->fail(context.getString(R.string.registration_timeout));
    }
    static void send(Context context,Intent request){
        try{
            GoogleRegistration registration=new GoogleRegistration(context,request);
            registration.handler.post(registration::bind);
        }catch(Exception unavailable){context.sendBroadcast(request);}
    }
    private void bind(){
        try{
            bound=context.bindService(new Intent("com.google.android.c2dm.intent.REGISTER").setPackage("com.google.android.gms"),this,Context.BIND_AUTO_CREATE);
            if(!bound){legacy();return;}
            handler.postDelayed(timeout,20000);
        }catch(RuntimeException unavailable){legacy();}
    }
    @Override public void onServiceConnected(ComponentName component,IBinder binder){
        if(finished)return;
        try{
            Bundle data=new Bundle();
            for(String name:new String[]{"scope","sender","subscription","X-subscription","subtype","X-subtype"})
                data.putString(name,original.getStringExtra(name));
            Bundle request=new Bundle();request.putBoolean("oneWay",false);request.putString("pkg",context.getPackageName());request.putBundle("data",data);
            Message message=Message.obtain();message.what=1;message.arg1=requestId;
            message.replyTo=new Messenger(new Handler(Looper.getMainLooper(),this::receive));message.setData(request);
            new Messenger(binder).send(message);
        }catch(Exception unavailable){legacy();}
    }
    private boolean receive(Message message){
        // The Messenger Binder stamps the sending UID; an unrelated app cannot provide a token.
        if(finished||message.sendingUid!=googleUid||message.arg1!=requestId)return true;
        Bundle response=message.getData();
        if(response.getBoolean("unsupported",false)){legacy();return true;}
        Bundle data=response.getBundle("data");
        String token=data==null?null:data.getString("registration_id");
        if(token==null||token.isEmpty()){
            String error=data==null?null:data.getString("error");
            fail(error!=null&&error.matches("[A-Z_0-9]{1,64}")?error:"GOOGLE_REGISTRATION_FAILED");return true;
        }
        if(token.length()>4096||token.indexOf('\n')>=0||token.indexOf('\r')>=0){fail("GOOGLE_REGISTRATION_FAILED");return true;}
        String vapid=original.getStringExtra("sender");
        finish();
        if(!vapid.equals(Config.prefs(context).getString("requestedVapid","")))return true;
        // Adapt the RPC result to the library's existing protected registration callback.
        new FirebaseReceiver().onReceive(context,new Intent("com.google.android.c2dm.intent.REGISTRATION")
            .putExtra("registration_id",original.getStringExtra("kid")+":"+token));
        return true;
    }
    private void fail(String reason){
        if(finished)return;finish();
        if(original.getStringExtra("sender").equals(Config.prefs(context).getString("requestedVapid","")))PushRegistration.failed(reason);
    }
    private void legacy(){if(finished)return;finish();context.sendBroadcast(original);}
    private void finish(){finished=true;handler.removeCallbacks(timeout);if(bound){bound=false;try{context.unbindService(this);}catch(IllegalArgumentException ignored){}}}
    @Override public void onServiceDisconnected(ComponentName component){fail("GOOGLE_REGISTRATION_DISCONNECTED");}
    @Override public void onBindingDied(ComponentName component){fail("GOOGLE_REGISTRATION_DISCONNECTED");}
    @Override public void onNullBinding(ComponentName component){legacy();}
}
