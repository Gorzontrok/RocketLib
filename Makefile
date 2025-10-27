# RocketLib Makefile
# Minimal MSBuild wrapper - leverages Scripts/BroforceModBuild.targets for installation

MSBUILD := /mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe
MSBUILD_FLAGS := /p:Configuration=Release /verbosity:minimal /nologo

# LAUNCH variable controls both kill and launch behavior
# Usage: make LAUNCH=no
ifeq ($(LAUNCH),no)
	LAUNCH_FLAGS := /p:CloseBroforceOnBuild=false /p:LaunchBroforceOnBuild=false
else
	LAUNCH_FLAGS := /p:CloseBroforceOnBuild=true /p:LaunchBroforceOnBuild=true
endif

# Default target shows help
.DEFAULT_GOAL := help

.PHONY: help
help:
	@echo "RocketLib Build System"
	@echo ""
	@echo "Targets:"
	@echo "  make build              Build RocketLib (kill game, build, launch)"
	@echo "  make build-no-launch    Build without disrupting running game"
	@echo "  make clean              Clean build artifacts"
	@echo "  make rebuild            Clean and rebuild"
	@echo ""
	@echo "Options:"
	@echo "  LAUNCH=no               Don't kill or launch game"
	@echo ""
	@echo "Examples:"
	@echo "  make build              Standard build with game launch"
	@echo "  make build LAUNCH=no    Build without disrupting running game"

.PHONY: build
build:
	"$(MSBUILD)" RocketLib.sln $(MSBUILD_FLAGS) $(LAUNCH_FLAGS)

.PHONY: build-no-launch
build-no-launch:
	"$(MSBUILD)" RocketLib.sln $(MSBUILD_FLAGS) /p:CloseBroforceOnBuild=false /p:LaunchBroforceOnBuild=false

.PHONY: clean
clean:
	"$(MSBUILD)" RocketLib.sln /t:Clean $(MSBUILD_FLAGS)

.PHONY: rebuild
rebuild: clean
	"$(MSBUILD)" RocketLib.sln $(MSBUILD_FLAGS) /p:CloseBroforceOnBuild=false /p:LaunchBroforceOnBuild=false
