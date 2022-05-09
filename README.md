# MyReaderWriterLock

一个简易的读写锁，具有以下5个特点：
1. 支持同时多个线程进行并发的读访问
2. 支持多个线程进行写请求
3. 写优先，不会出现写饥饿现象
4. 非公平，锁的获取顺序不一定符合请求的绝对顺序
5. 可重入，允许读锁重入读锁、读锁重入写锁、写锁重入写锁，但不允许写锁重入读锁
