﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebSockets
{
    public partial class Form1 : Form
    {
        GameEngine engine;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            WebSocketServer server = new WebSocketServer();
            server.toWrite = (string str) =>
                {
                    this.textBox1.Invoke(new Action(() => 
                    {
                        this.textBox1.AppendText(str + Environment.NewLine);
                    }));
                };
            server.onClientJoined = (WebSocketClient client) =>
            {

            };
        }
    }
}
