package com.colin.clipbridge;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.Intent;
import org.unifiedpush.android.embedded_fcm_distributor.EmbeddedDistributorReceiver;

/** Keep UnifiedPush's registration and encryption; use the current Google RPC transport. */
public final class EmbeddedPushReceiver extends EmbeddedDistributorReceiver {
    @Override public void onReceive(Context context,Intent intent){
        super.onReceive(new ContextWrapper(context){
            @Override public void sendBroadcast(Intent request){
                if("com.google.iid.TOKEN_REQUEST".equals(request.getAction())&&"com.google.android.gms".equals(request.getPackage()))
                    GoogleRegistration.send(getApplicationContext(),request);
                else super.sendBroadcast(request);
            }
        },intent);
    }
}
