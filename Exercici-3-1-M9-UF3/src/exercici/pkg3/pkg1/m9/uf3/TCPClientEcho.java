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
        //Es crea el socket.
        this.socket = new Socket("localhost", 5487);
        
        outToServer = new DataOutputStream(socket.getOutputStream());
        
        //Es crea el buffer
        buffer = new BufferedReader(new InputStreamReader(socket.getInputStream()));

        //Enviem al servidor un missatge
        outToServer.writeBytes("hola \n");
        
        //Esta atent per quan rep un missatge ficar-lo al String cadena.
        String cadena = buffer.readLine();

        System.out.println(cadena);

        JOptionPane.showMessageDialog(null, cadena);
        
        //Es tanquen el buffer, socket i el dataoutput stream
        outToServer.close();
        buffer.close();
        socket.close();
               
    }

    public static void main(String[] args) throws IOException {
        new TCPClientEcho(hostname, port);

    }
}
