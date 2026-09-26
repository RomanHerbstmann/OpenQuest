// Each collection boots its own API on its own database. The API reads process-wide environment variables while
// starting, so the collections must not start concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
