package exercici.pkg3.pkg1.m9.uf3;

import java.io.BufferedReader;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.Socket;
import javax.swing.JOptionPane;

public class TCPClientEcho {

    Socket socket;
    DataOutputStream outToServer;
    BufferedReader buffer;
    static String hostname = "localhost";
    static int port = 2020;

    public TCPClientEcho(String host, int port) throws IOException {
        this.socket = new Socket("localhost", 5487);
        outToServer = new DataOutputStream(socket.getOutputStream());
        buffer = new BufferedReader(new InputStreamReader(socket.getInputStream()));

        outToServer.writeBytes("hola \n");
        String cadena = buffer.readLine();

        System.out.println(cadena);

        JOptionPane.showMessageDialog(null, cadena);
        
                outToServer.close();
        buffer.close();
        socket.close();
               
    }

    public static void main(String[] args) throws IOException {
        new TCPClientEcho(hostname, port);

    }
}
