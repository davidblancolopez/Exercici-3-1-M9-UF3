package exercici.pkg3.pkg1.m9.uf3;

import java.io.BufferedReader;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.ServerSocket;
import java.net.Socket;
import javax.swing.JOptionPane;

public class TCPServerEcho {

    static int port = 5487;
    ServerSocket ssocket;

    public TCPServerEcho(int port) throws IOException {
        this.ssocket = new ServerSocket(port);

        while (true) {
            Socket accept = ssocket.accept();
            DataOutputStream outToClient = new DataOutputStream(accept.getOutputStream());
            BufferedReader buffer = new BufferedReader(new InputStreamReader(accept.getInputStream()));
            String cadena = buffer.readLine();
            outToClient.writeBytes(cadena + "\n");
            JOptionPane.showMessageDialog(null, cadena);

            outToClient.close();
            buffer.close();
        }

    }

    public static void main(String[] args) throws IOException {
        new TCPServerEcho(port);
    }

}
