        private void RegisterSystems()
        {
            _systems.Clear();
            _systems.AddRange(EcsSystemPipeline.BuildUpdatePipeline(_serviceProvider));
        }
