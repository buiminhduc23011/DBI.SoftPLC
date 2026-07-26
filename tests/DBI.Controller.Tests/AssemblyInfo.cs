// Test đo jitter và chu kỳ quét thật. Chạy song song với các collection khác sẽ có test khác
// đốt CPU cùng lúc, và số đo mất hết ý nghĩa. Cả bộ test chỉ mất vài giây nên tắt song song là
// cái giá rẻ để giữ được phép đo determinism đáng tin.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
