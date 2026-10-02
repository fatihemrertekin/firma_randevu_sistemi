using Xunit;

namespace Server.Tests;

// Bu testler eskiden tek partial sınıfta sırayla çalışıyordu. Komut testlerinin
// işlem genelindeki ortam değişkenleri ayrı sınıflarda da birbirini etkilemesin.
// Senaryo içindeki paralel istekler ve her testin ayrı PostgreSQL'i korunur.
[CollectionDefinition(Name)]
public sealed class AuthenticationTestCollection
{
    public const string Name = "Identity integration";
}
