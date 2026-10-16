package com.colin.clipbridge;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

/** Uses the Android app's actual crypto class; test secrets remain in the parent's private pipe. */
public final class PairingCrossCheck {
    public static void main(String[] args)throws Exception {
        PairingIdentity identity=new PairingIdentity();
        System.out.println(identity.publicKey()+" "+identity.nonce);System.out.flush();
        String[] input=new BufferedReader(new InputStreamReader(System.in,StandardCharsets.UTF_8)).readLine().split(" ",2);
        String transcript=new String(Base64.getDecoder().decode(input[1]),StandardCharsets.UTF_8);
        String key=identity.agree(input[0],transcript);
        System.out.println(key+" "+PairingIdentity.code(key));
    }
}
