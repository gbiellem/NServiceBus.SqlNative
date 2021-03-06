﻿using System;
using System.IO;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Attachments.Sql;
using SampleNamespace;

class MyHandler :
    IHandleMessages<SampleMessage>
{
    public Task Handle(SampleMessage message, IMessageHandlerContext context)
    {
        Console.WriteLine("MyHandler");
        foreach (var header in context.MessageHeaders)
        {
            Console.WriteLine($"{header.Key.Replace("NServiceBus.","")}={header.Value}");
        }
        return context.Attachments().ProcessStreams(WriteAttachment);
    }

    static async Task WriteAttachment(AttachmentStream stream)
    {
        using var reader = new StreamReader(stream);
        var contents = await reader.ReadToEndAsync();
        Console.WriteLine("Attachment: {0}. Contents:{1}", stream.Name, contents);
    }
}