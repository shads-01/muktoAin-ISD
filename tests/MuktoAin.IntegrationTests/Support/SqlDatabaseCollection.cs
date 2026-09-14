using Xunit;

namespace MuktoAin.IntegrationTests.Support;

// Single shared, serialized database for all T-3.3 test classes.
[CollectionDefinition("MuktoAinSqlDb")]
public class SqlDatabaseCollection : ICollectionFixture<SqlDatabaseFixture>
{
}
