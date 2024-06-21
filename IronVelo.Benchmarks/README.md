# IronVelo SDK Benchmarks

This project contains performance benchmarks for various components of the IronVelo SDK, using 
[BenchmarkDotNet](https://benchmarkdotnet.org/).

## Overview

The benchmarks are designed to measure the performance of critical operations in the SDK, including Base64 
encoding/decoding, password validation, and OTP validation. The results help us ensure the SDK is optimized for high 
performance requirements.

## Benchmark Categories

1. **Base64 Encoding and Decoding**
2. **Password Validation**
3. **OTP Validation**
4. **Numeric Validation**

## Getting Started

### Prerequisites

- .NET SDK (Download from [here](https://dotnet.microsoft.com/download))
- BenchmarkDotNet library (included in the project)

### Running the Benchmarks

To run the benchmarks, navigate to the `Benchmarks` directory and execute the benchmarks using the .NET CLI.

```sh
cd Benchmarks
dotnet run -- --bench <BENCH NAME>
```

### Available Benchmarks

- **Base64Decode:** Benchmarks for Base64 decoding methods.
- **Base64Encode:** Benchmarks for Base64 encoding methods.
- **Password:** Benchmarks for password validation methods.
- **Otp:** Benchmarks for OTP (One-Time Password) validation methods.
- **IsNumeric:** Benchmarks for numeric validation methods.

### Example Commands

```sh
# Run Base64 decode benchmarks
dotnet run -- --bench Base64Decode

# Run password validation benchmarks
dotnet run -- --bench Password
```

### Listing All Benchmarks

```sh
dotnet run -- --list
```

### Configuration
The benchmark configuration is defined in the `BenchmarksConfigSer` class. This configuration includes:

- **Memory Diagnoser:** To measure memory allocation.
- **Console Logger:** To log benchmark results to the console.
- **Columns:** To include method, median, standard deviation, first quartile (Q1), and third quartile (Q3) in the results.