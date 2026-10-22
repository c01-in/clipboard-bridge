package com.colin.clipbridge;

import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.nio.charset.StandardCharsets;

/** Exercises the actual Android HTTP transport over loopback; no Android runtime or emulator. */
public final class HttpTransportChecks {
    static void require(boolean ok,String message){if(!ok)throw new AssertionError(message);}
    public static void main(String[] args)throws Exception{
        HttpServer server=HttpServer.create(new InetSocketAddress("127.0.0.1",0),0);
        byte[] body="{\"name\":\"测试.pdf\"}".getBytes(StandardCharsets.UTF_8);
        int[] successes={0};
        server.createContext("/ok",exchange->{
            byte[] received=exchange.getRequestBody().readAllBytes();
            boolean correct="POST".equals(exchange.getRequestMethod())&&java.util.Arrays.equals(body,received)
                &&Integer.toString(body.length).equals(exchange.getRequestHeaders().getFirst("Content-Length"));
            byte[] response=(correct?"accepted":"bad body").getBytes(StandardCharsets.UTF_8);
            exchange.sendResponseHeaders(correct?200:400,response.length);
            try(var output=exchange.getResponseBody()){output.write(response);}successes[0]++;
        });
        server.createContext("/reject",exchange->{exchange.getRequestBody().close();exchange.sendResponseHeaders(400,-1);exchange.close();});
        server.createContext("/redirect",exchange->{exchange.getRequestBody().close();exchange.getResponseHeaders().set("Location","/ok");exchange.sendResponseHeaders(302,-1);exchange.close();});
        server.createContext("/large",exchange->{exchange.getRequestBody().close();exchange.sendResponseHeaders(200,4096);try(var output=exchange.getResponseBody()){output.write(new byte[4096]);}});
        server.start();
        try{
            String base="http://127.0.0.1:"+server.getAddress().getPort(),unreachable;
            try(ServerSocket socket=new ServerSocket(0,1,java.net.InetAddress.getLoopbackAddress())){unreachable="http://127.0.0.1:"+socket.getLocalPort();}
            require("accepted".equals(HttpTransport.request(new String[]{base},"/ok",new String(body,StandardCharsets.UTF_8),1024,1000)),"UTF-8 body or byte Content-Length changed");
            try{HttpTransport.request(new String[]{base,unreachable},"/reject","{}",1024,1000);throw new AssertionError("400 was accepted");}
            catch(HttpTransport.HttpFailure expected){require(expected.status==400,"A stale endpoint masked HTTP 400");}
            require("accepted".equals(HttpTransport.request(new String[]{unreachable,base},"/ok",new String(body,StandardCharsets.UTF_8),1024,1000)),"An unreachable endpoint prevented fallback");
            require("accepted".equals(HttpTransport.request(new String[]{base+"/reject",base},"/ok",new String(body,StandardCharsets.UTF_8),1024,1000)),"An HTTP rejection prevented fallback to a valid endpoint");
            try{HttpTransport.request(new String[]{unreachable},"/ok",null,1024,1000);throw new AssertionError("Disconnected endpoint was accepted");}
            catch(HttpTransport.HttpFailure wrong){throw new AssertionError("Connection failure was called an HTTP rejection",wrong);}
            catch(IOException expected){}
            try{HttpTransport.request(new String[]{base,unreachable},"/large",null,16,1000);throw new AssertionError("Response size limit was ignored");}
            catch(HttpTransport.ResponseTooLarge expected){}
            int before=successes[0];
            try{HttpTransport.request(new String[]{base},"/redirect","{}",1024,1000);throw new AssertionError("Redirect was followed");}
            catch(HttpTransport.HttpFailure expected){require(expected.status==302&&successes[0]==before,"Encrypted body followed a redirect");}
            try{HttpTransport.request(new String[]{"http://user@127.0.0.1"},"/ok",null,1024,1000);throw new AssertionError("Credentials in URL accepted");}
            catch(HttpTransport.InvalidAddress expected){}
            System.out.println("HTTP rejection, connectivity, fallback, UTF-8 framing, response bounds and redirect checks passed");
        }finally{server.stop(0);}
    }
}
