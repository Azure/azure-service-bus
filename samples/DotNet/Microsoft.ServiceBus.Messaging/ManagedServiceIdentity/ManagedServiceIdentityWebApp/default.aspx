<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="default.aspx.cs" Inherits="ManagedServiceIdentityWebApp._Default" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Service Bus Managed Service Identity Demo</title>
    <link rel="stylesheet" href="StyleSheet.css" />
</head>
<body>
    <form id="form1" runat="server">
        <div style="white-space: pre">
            <div>
                <label>Service Bus Endpoint FQDN</label><asp:TextBox ID="txtNamespace" runat="server" Text="" />
            </div>
            <div>
                <label>Queue Name</label><asp:TextBox ID="txtQueueName" runat="server" Text=""/>
            </div>
            <div>
                <label>Data to Send </label> <asp:TextBox ID="txtData" runat="server" TextMode="MultiLine" Width="500px"/>
            </div>
            <div>
                <asp:Button ID="btnSend" runat="server" Text="Send" OnClick="btnSend_Click" /> <asp:Button ID="btnReceive" runat="server" Text="Receive" OnClick="btnReceive_Click" />
            </div>            
            <div>
                <label>Received Data </label> <asp:TextBox ID="txtReceivedData" runat="server" Enabled="false" TextMode="MultiLine" Width="500px" Height="100px" />
                <asp:HiddenField ID="hiddenStartingOffset" runat="server" />
            </div>
        </div>
    </form>
</body>
</html>
