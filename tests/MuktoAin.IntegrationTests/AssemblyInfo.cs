using Xunit;

// Disable parallelization across integration tests to avoid port/resource contention
// and multiple WebApplicationFactory hosts spinning up concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
