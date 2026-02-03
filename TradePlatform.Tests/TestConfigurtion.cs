// Disable parallel test execution for the Integration Test suite.
// This prevents the xUnit Runner race condition ("Key already exists")
// and ensures Docker containers don't exhaust system resources.
[assembly: CollectionBehavior(DisableTestParallelization = true)]