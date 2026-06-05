using System;

namespace SantiyeAPI.Exceptions; // 🚨 DİKKAT: Burası Exceptions!

public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entityName, object id) 
        : base($"{entityName} bulunamadı. ID: {id}") { }
}