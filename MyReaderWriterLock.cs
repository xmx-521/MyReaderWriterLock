using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MyReaderWriterLock
{
    /*
     * 可重入读写锁
     * 该锁有以下5个特点：
     * 1. 支持同时多个线程进行并发的读访问
     * 2. 支持多个线程进行写请求
     * 3. 写优先，不会出现写饥饿现象
     * 4. 非公平，锁的获取顺序不一定符合请求的绝对顺序
     * 5. 可重入，允许读锁重入读锁、读锁重入写锁、写锁重入写锁，但不允许写锁重入读锁）
     */
    public class MyReentrantReaderWriterLock
    {
        private long _readLockOccupyCount; // 读锁占用量
        private long _writeLockOccupyCount; // 写锁占用量
        [ThreadStatic]
        private static int _localReadLockOccupyCount; // 本线程读锁占用量
        private int _waitingWriterCount; // 等待占用写锁的线程数
        private int _exclusiveThreadId = -1; // 独占写锁的线程id，在共享读锁的状态下为-1
        private readonly object _stateLock = new(); // 被Monitor用于保护以上状态变量

        /*
         * 进入读锁
         */
        public void EnterReadLock()
        {
            var acquiredLock = false;
            try
            {
                Monitor.Enter(_stateLock, ref acquiredLock);
                /*
                 * 若满足以下两个条件之一：
                 * 1. 当前线程id与独占写锁的线程id相同(说明读锁重入写锁)
                 * 2. 本地读锁占有量大于0(说明读锁重入读锁，也有可能是首先读锁重入写锁，然后读锁再重入读锁))
                 */
                if (Environment.CurrentManagedThreadId == _exclusiveThreadId || _localReadLockOccupyCount > 0)
                {
                    // 发生重入，读锁占用量加1
                    _readLockOccupyCount++;
                    _localReadLockOccupyCount++;
                    return;
                }

                /*
                 * 非读锁重入写锁，若满足下列两个条件之一：
                 * 1. 存在线程占用写锁(_writeLockOccupyCount != 0)
                 * 2. 存在线程等待占用写锁(实现写优先)(_waitingWriterCount != 0)
                 * 就释放当前线程占用的对象锁，进入等待状态，阻塞当前线程直到被唤醒
                 */
                while (_writeLockOccupyCount != 0 || _waitingWriterCount != 0)
                {
                    Monitor.Wait(_stateLock);
                }

                // 线程占用读锁，读锁占用量加1
                _readLockOccupyCount++;
                _localReadLockOccupyCount++;
            }
            finally
            {
                if (acquiredLock)
                {
                    Monitor.Exit(_stateLock);
                }
            }
        }

        /*
         * 退出读锁
         */
        public void ExitReadLock()
        {
            var acquiredLock = false;
            try
            {
                Monitor.Enter(_stateLock, ref acquiredLock);
                _readLockOccupyCount--;
                _localReadLockOccupyCount--;

                /*
                 * 若满足以下两个条件之一：
                 * 1. 当前线程id与独占写锁的线程id相同(说明此处释放的是重入写锁的读锁)
                 * 2. 本地读锁占有量大于等于1(说明此处释放的是重入读锁的读锁，也有可能是首先读锁重入写锁，然后读锁再重入读锁))
                 */
                if (Environment.CurrentManagedThreadId == _exclusiveThreadId || _localReadLockOccupyCount >= 1)
                {
                    return;
                }

                /*
                 * 若同时满足以下两个条件：
                 * 1. 没有线程占用读锁
                 * 2. 存在等待占用写锁的线程
                 * 则唤醒一个等待占用写锁的线程
                 */
                if (_readLockOccupyCount == 0 && _waitingWriterCount > 0)
                {
                    Monitor.Pulse(_stateLock);
                }
            }
            finally
            {
                if (acquiredLock)
                {
                    Monitor.Exit(_stateLock);
                }
            }
        }

        /*
         * 进入写锁
         */
        public void EnterWriteLock()
        {
            var acquiredLock = false;
            try
            {
                Monitor.Enter(_stateLock, ref acquiredLock);
                /*
                 * 若当前线程id与独占写锁的线程id相同
                 * 则说明出现写锁重入写锁的情况
                 */
                if (Environment.CurrentManagedThreadId == _exclusiveThreadId)
                {
                    /*
                     * 若读锁占用数不为0，说明发生了写锁重入读锁
                     * 这样的情况不允许发生，应抛出异常
                     */
                    if (_readLockOccupyCount != 0)
                    {
                        throw new Exception("写锁不可重入读锁");
                    }

                    // 写锁重入写锁，写锁占用量加1
                    _writeLockOccupyCount += 1;
                    return;
                }

                _waitingWriterCount++; // 等待占用写锁的线程数加1

                /*
                 * 非写锁重入写锁，若满足下列两个条件之一：
                 * 1. 存在其它线程占用读锁(_readLockOccupyCount != 0)
                 * 2. 存在其它线程占用写锁(_writeLockOccupyCount != 0)
                 * 就释放当前线程占用的对象锁，进入等待状态，阻塞当前线程直到被唤醒
                 */
                while (_readLockOccupyCount != 0 || _writeLockOccupyCount != 0)
                {
                    Monitor.Wait(_stateLock);
                }

                _waitingWriterCount--; // 线程占用写锁，等待占用写锁的线程数减1
                _writeLockOccupyCount += 1; // 写锁占用量加1
                _exclusiveThreadId = Environment.CurrentManagedThreadId; // 设置独占写锁的线程id为当前线程id
            }
            finally
            {
                if (acquiredLock)
                {
                    Monitor.Exit(_stateLock);
                }
            }
        }

        /*
         * 退出写锁
         */
        public void ExitWriteLock()
        {
            var acquireLock = false;
            try
            {
                Monitor.Enter(_stateLock, ref acquireLock);
                /*
                 * 若当前线程id不等于独占写锁的线程id
                 * 则说明存在多个线程占用写锁，应抛出异常
                 */
                if (Environment.CurrentManagedThreadId != _exclusiveThreadId)
                {
                    throw new Exception("存在多个线程占用写锁");
                }

                _writeLockOccupyCount--; // 写锁占用量减1

                /*
                 * 若当前写锁占用量不为0，则说明此处释放的是重入写锁的写锁
                 */
                if (_writeLockOccupyCount != 0)
                {
                    return;
                }

                /*
                 * 若当前写锁占用量为0（释放的写锁并非重入），则唤醒所有等待线程
                 * 若读者被唤醒且此时有写者在等待，则读者的while条件判断为true，将重新调用Wait方法阻塞
                 * 否则读者占用读锁
                 */
                _exclusiveThreadId = -1; // 设置独占写锁的线程id为空(-1)
                Monitor.PulseAll(_stateLock);
            }
            finally
            {
                if (acquireLock)
                {
                    Monitor.Exit(_stateLock);
                }
            }
        }
    }

    /*
     * 测试用例1
     * 该测试用例来自微软官方文档ReaderWriterLockSlim类的使用示例:
     * https://docs.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim?view=net-6.0
     * 首先定义了一个简单的同步缓存，该缓存包含整数键的字符串
     * 官方文档中使用ReaderWriterLockSlim实例用于同步，对充当内部缓存的Dictionary<TKey, TValue>访问
     * 我们将ReaderWriterLockSlim替换为自己实现的可重入锁MyReentrantReaderWriterLock
     * 并测试缓存的写入与读取，以验证读写锁的正确性
     * 预期输出可见官方文档，或文件末注释(实际输出结果未不需要与官方文档完全一致，满足正确性约束即可)
     */
    public class TestCaseOne
    {
        /*
         * 一个简单的同步缓存，使用MyReentrantReaderWriterLock作为读写锁
         * 用于之后的测试
         */
        private class SynchronizedCache
        {
            private MyReentrantReaderWriterLock cacheLock = new MyReentrantReaderWriterLock();
            private Dictionary<int, string> innerCache = new Dictionary<int, string>();

            public int Count
            {
                get { return innerCache.Count; }
            }

            // 读取缓存数据
            public string Read(int key)
            {
                cacheLock.EnterReadLock();
                try
                {
                    return innerCache[key];
                }
                finally
                {
                    cacheLock.ExitReadLock();
                }
            }

            // 添加缓存数据
            public void Add(int key, string value)
            {
                cacheLock.EnterWriteLock();
                try
                {
                    innerCache.Add(key, value);
                }
                finally
                {
                    cacheLock.ExitWriteLock();
                }
            }
        }

        public static void RunTest()
        {
            Console.WriteLine("-------------------------------------------");
            Console.WriteLine("[ 开始测试用例1 ]");
            var sc = new SynchronizedCache();
            var tasks = new List<Task>();
            int itemsWritten = 0;

            /*
             * 创建一个写者线程
             * 将17种vegetables写入缓存
             * 并最终输出写入结果
             */
            tasks.Add(Task.Run(() =>
            {
                String[] vegetables =
                {
                    "broccoli", "cauliflower",
                    "carrot", "sorrel", "baby turnip",
                    "beet", "brussel sprout",
                    "cabbage", "plantain",
                    "spinach", "grape leaves",
                    "lime leaves", "corn",
                    "radish", "cucumber",
                    "raddichio", "lima beans"
                };
                for (int ctr = 1; ctr <= vegetables.Length; ctr++)
                {
                    sc.Add(ctr, vegetables[ctr - 1]);
                }

                itemsWritten = vegetables.Length;
                Console.WriteLine("Task {0} wrote {1} items\n", Task.CurrentId, itemsWritten);
            }));

            /*
             * 创建两个读线程
             * 第一个读线程从前向后遍历已写入的缓存
             * 第二个线程从后向前遍历已写入的缓存
             */
            for (int ctr = 0; ctr <= 1; ctr++)
            {
                bool desc = ctr == 1;
                tasks.Add(Task.Run(() =>
                {
                    int start, last, step;
                    int items;
                    do
                    {
                        String output = String.Empty;
                        items = sc.Count;
                        if (!desc)
                        {
                            start = 1;
                            step = 1;
                            last = items;
                        }
                        else
                        {
                            start = items;
                            step = -1;
                            last = 1;
                        }

                        for (int index = start; desc ? index >= last : index <= last; index += step)
                        {
                            output += String.Format("[{0}] ", sc.Read(index));
                        }

                        Console.WriteLine("Task {0} read {1} items: {2}\n",
                            Task.CurrentId, items, output);
                    } while (items < itemsWritten | itemsWritten == 0);
                }));
            }

            // 等待全部的Tasks执行完毕
            Task.WaitAll(tasks.ToArray());

            // 展示最终执行结果
            Console.WriteLine();
            Console.WriteLine("Values in synchronized cache: ");
            for (int ctr = 1; ctr <= sc.Count; ctr++)
            {
                Console.WriteLine(" {0}: {1}", ctr, sc.Read(ctr));
            }

            Console.WriteLine("[ 测试用例1成功 ]");
        }
    }

    /*
     * 测试用例2
     * 该测试用例通过读写锁，控制对于counter计数器交替的写入与读取
     * 可以反映本读写锁的四大特点：
     * 1. 支持同时多个线程进行并发的读访问
     * 2. 支持多个线程进行写请求
     * 3. 写优先，不会出现写饥饿现象
     * 4. 非公平，锁的获取顺序不一定符合请求的绝对顺序
     * 测试用例预期输出可见文件末注释
     */
    public class TestCaseTwo
    {
        private static MyReentrantReaderWriterLock _counterLock = new MyReentrantReaderWriterLock();
        private static int _counter = 0;
        private const int HalfReadThreadNum = 5;
        private const int HalfWriteThreadNum = 5;

        /*
         * 进行一次读操作
         * 单次读操作的用时为1s
         */
        static void Read(string name)
        {
            try
            {
                _counterLock.EnterReadLock();
                Thread.Sleep(TimeSpan.FromSeconds(1));
                Console.WriteLine($"{name} 读出counter大小: {_counter}");
            }
            finally
            {
                _counterLock.ExitReadLock();
            }
        }

        /*
         * 进行一次写操作
         * 单次写操作的用时为1s
         */
        static void Write(string name)
        {
            try
            {
                _counterLock.EnterWriteLock();
                Thread.Sleep(TimeSpan.FromSeconds(1));
                _counter++;
                Console.WriteLine($"{name} 增加counter的大小为: {_counter}");
            }
            finally
            {
                _counterLock.ExitWriteLock();
            }
        }

        public static void RunTest()
        {
            Console.WriteLine("-------------------------------------------");
            Console.WriteLine("[ 开始测试用例2 ]");
            var tasks = new List<Task>();   // 存储全部的任务（线程）

            /*
             * 计时器用于计时
             * 用于证明支持并发读访问
             */
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // 启动HalfReadThreadNum个读者线程
            for (int i = 1; i <= HalfReadThreadNum; i++)
            {
                int readThreadId = i;
                tasks.Add(Task.Run(() => Read($"Read Thread {readThreadId}")));
            }

            /*
             * 使主线程等待0.2s，确保之后的一组写者线程在前面的一组读者线程后启动
             * 由于0.2s小于读者进行读操作的时间(1s)，因此可保证在读者完成读操作之前启动写者线程
             */
            Thread.Sleep(TimeSpan.FromSeconds(0.2));

            // 启动HalfWriteThreadNum个写者线程
            for (int i = 1; i <= HalfWriteThreadNum; i++)
            {
                int writeThreadId = i;
                tasks.Add(Task.Run(() => Write($"Write Thread {writeThreadId}")));
            }

            /*
             * 使主线程等待0.2s，确保之后的一组读者线程在前面的一组写者线程后启动
             * 由于0.2s + 0.2s小于读者进行读操作的时间(1s)，因此可保证在读者完成读操作之前启动另一组读者线程
             */
            Thread.Sleep(TimeSpan.FromSeconds(0.2));

            // 启动HalfReadThreadNum个读者线程
            for (int i = HalfReadThreadNum + 1; i <= 2 * HalfReadThreadNum; i++)
            {
                int readThreadId = i;
                tasks.Add(Task.Run(() => Read($"Read Thread {readThreadId}")));
            }

            /*
             * 使主线程等待0.2s，确保之后的一组写者线程在前面的一组读者线程后启动
             * 由于0.2s + 0.2s + 0.2s小于读者进行读操作的时间(1s)，因此可保证在读者完成读操作之前启动另一组写者线程
             */
            Thread.Sleep(TimeSpan.FromSeconds(0.2));

            // 启动HalfWriteThreadNum个写者线程
            for (int i = HalfWriteThreadNum + 1; i <= 2 * HalfWriteThreadNum; i++)
            {
                int writeThreadId = i;
                tasks.Add(Task.Run(() => Write($"Write Thread {writeThreadId}")));
            }

            /*
             * 当全部线程成功启动时输出
             * 应保证输出于读者完成读操作的输出之前
             */
            Console.WriteLine($"全部线程启动完毕");

            // 等待全部线程执行完毕，输出counter大小与总用时
            Task.WaitAll(tasks.ToArray());
            Console.WriteLine($"counter 最终大小为: {_counter}");

            stopwatch.Stop();
            Console.WriteLine($"总用时为: {stopwatch.Elapsed.Seconds} s");

            // 若counter大小与预期一致，输出测试成功，否则测试失败
            if (_counter == HalfWriteThreadNum * 2)
            {
                Console.WriteLine("[ 测试用例2成功 ]");
            }
            else
            {
                Console.WriteLine("[ 测试用例2失败 ]");
            }
        }
    }

    /*
     * 测试用例3
     * 该测试用例在测试用例2的基础上增加了一些复杂性
     * 将简单的读、写操作替换为了较为复杂的：
     * 1. 读锁重入读锁
     * 2. 读锁重入写锁
     * 3. 写锁重入读锁
     * （不支持写锁重入读锁）
     * 既测试了这些重入方式能否单独正确工作
     * 也测试了他们之间进行复杂组合时，是否仍能保持正确性
     */
    public class TestCaseThree
    {
        private static MyReentrantReaderWriterLock _counterLock = new MyReentrantReaderWriterLock();
        private static int _counter = 0;
        private const int HalfReadReadThreadNum = 5;
        private const int ReadWriteThreadNum = 5;
        private const int WriteWriteThreadNum = 5;

        /*
         * 读锁重入读锁操作
         * 用时为1s + 1s = 2s
         */
        private static void ReadLockReEnterReadLock(string name)
        {
            try
            {
                _counterLock.EnterReadLock();
                Thread.Sleep(TimeSpan.FromSeconds(1));
                Console.WriteLine($"{name} 读出counter大小: {_counter}");
                // 读锁重入读锁
                try
                {
                    _counterLock.EnterReadLock();
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    Console.WriteLine($"{name} 读出counter大小: {_counter}");
                }
                finally
                {
                    _counterLock.ExitReadLock();
                }
            }
            finally
            {
                _counterLock.ExitReadLock();
            }
        }

        /*
         * 读锁重入写锁操作
         * 用时为1s + 1s = 2s
         */
        private static void ReadLockReEnterWriteLock(string name)
        {
            try
            {
                _counterLock.EnterWriteLock();
                Thread.Sleep(TimeSpan.FromSeconds(1));
                _counter++;
                Console.WriteLine($"{name} 增加counter的大小为: {_counter}");
                try
                {
                    _counterLock.EnterReadLock();
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    Console.WriteLine($"{name} 读出counter大小: {_counter}");
                }
                finally
                {
                    _counterLock.ExitReadLock();
                }
            }
            finally
            {
                _counterLock.ExitWriteLock();
            }
        }

        /*
         * 写锁重入写锁操作
         * 用时为1s + 1s = 2s
         */
        private static void WriteLockReEnterWriteLock(string name)
        {
            try
            {
                _counterLock.EnterWriteLock();
                Thread.Sleep(TimeSpan.FromSeconds(1));
                _counter++;
                Console.WriteLine($"{name} 增加counter的大小为: {_counter}");
                try
                {
                    _counterLock.EnterWriteLock();
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    _counter++;
                    Console.WriteLine($"{name} 增加counter的大小为: {_counter}");
                }
                finally
                {
                    _counterLock.ExitWriteLock();
                }
            }
            finally
            {
                _counterLock.ExitWriteLock();
            }
        }

        public static void RunTest()
        {
            Console.WriteLine("-------------------------------------------");
            Console.WriteLine("[ 开始测试用例3 ]");
            var tasks = new List<Task>();

            /*
             * 计时器用于计时
             * 用于证明支持并发读访问
             */
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // 启动HalfReadReadThreadNum个读锁重入读锁的线程
            for (int i = 1; i <= HalfReadReadThreadNum; i++)
            {
                int readReadThreadId = i;
                tasks.Add(Task.Run(() =>
                    ReadLockReEnterReadLock($"Read lock reenter Read lock Thread {readReadThreadId}")));
            }

            /*
             * 使主线程等待0.2s，确保之后的一组读锁重入写锁的线程在前面的一组读锁重入读锁的线程后启动
             * 由于0.2s小于读者进行读操作的时间(1s + 1s)，因此可保证在读者完成读操作之前启动线程
             */
            Thread.Sleep(TimeSpan.FromSeconds(0.2));

            // 启动ReadWriteThreadNum个读锁重入写锁的线程
            for (int i = 1; i <= ReadWriteThreadNum; i++)
            {
                int readWriteThreadId = i;
                tasks.Add(Task.Run(() =>
                    ReadLockReEnterWriteLock($"Read lock reenter Write lock Thread {readWriteThreadId}")));
            }

            /*
             * 使主线程等待0.2s，确保之后的另一组读锁重入读锁线程在前面的一组读锁重入写锁线程后启动
             * 由于0.2s + 0.2s小于读者进行读操作的时间(1s)，因此可保证在读者完成读操作之前启动线程
             */
            Thread.Sleep(TimeSpan.FromSeconds(0.2));

            // 再启动HalfReadReadThreadNum个读锁重入读锁的线程
            for (int i = HalfReadReadThreadNum + 1; i <= 2 * HalfReadReadThreadNum; i++)
            {
                int readReadThreadId = i;
                tasks.Add(Task.Run(() =>
                    ReadLockReEnterReadLock($"Read lock reenter Read lock Thread {readReadThreadId}")));
            }

            /*
             * 使主线程等待0.2s，确保之后的一组写锁重入写锁线程在前面的另一组读锁重入读锁线程后启动
             * 由于0.2s + 0.2s + 0.2s小于读者进行读操作的时间(1s)，因此可保证在读者完成读操作之前启动线程
             */
            Thread.Sleep(TimeSpan.FromSeconds(0.2));
            for (int i = 1; i <= WriteWriteThreadNum; i++)
            {
                int writeWriteThreadId = i;
                tasks.Add(Task.Run(() =>
                    WriteLockReEnterWriteLock($"Write lock reenter Write lock Thread {writeWriteThreadId}")));
            }

            /*
             * 当全部线程成功启动时输出
             * 应保证输出于读者完成读操作的输出之前
             */
            Console.WriteLine($"全部线程启动完毕");

            // 等待全部线程执行完毕，输出counter大小与总用时
            Task.WaitAll(tasks.ToArray());
            Console.WriteLine($"counter 最终大小为: {_counter}");

            stopwatch.Stop();
            Console.WriteLine($"总用时为: {stopwatch.Elapsed.Seconds} s");

            // 若counter大小与预期一致，输出测试成功，否则测试失败
            if (_counter == ReadWriteThreadNum + WriteWriteThreadNum * 2)
            {
                Console.WriteLine("[ 测试用例3成功 ]");
            }
            else
            {
                Console.WriteLine("[ 测试用例3失败 ]");
            }
        }
    }

    /*
     * 运行测试用例的主类
     */
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("-------------------------------------------");
            Console.WriteLine("《开始测试MyReaderWriterLock》");
            TestCaseOne.RunTest();
            TestCaseTwo.RunTest();
            TestCaseThree.RunTest();
        }
    }
}
/*
 测试用例预期输出：
-------------------------------------------
《开始测试MyReaderWriterLock》
-------------------------------------------
[ 开始测试用例1 ]
Task 1 read 0 items: 

Task 2 read 0 items: 

Task 1 read 1 items: [broccoli] 

Task 2 read 1 items: [broccoli] 

Task 3 wrote 17 items

Task 1 read 17 items: [broccoli] [cauliflower] [carrot] [sorrel] [baby turnip] [beet] [brussel sprout] [cabbage] [plantain] [spinach] [grape leaves] [lime leaves] [corn] [radish] [cucumber] [raddichio] [lima beans] 

Task 2 read 17 items: [lima beans] [raddichio] [cucumber] [radish] [corn] [lime leaves] [grape leaves] [spinach] [plantain] [cabbage] [brussel sprout] [beet] [baby turnip] [sorrel] [carrot] [cauliflower] [broccoli] 


Values in synchronized cache: 
 1: broccoli
 2: cauliflower
 3: carrot
 4: sorrel
 5: baby turnip
 6: beet
 7: brussel sprout
 8: cabbage
 9: plantain
 10: spinach
 11: grape leaves
 12: lime leaves
 13: corn
 14: radish
 15: cucumber
 16: raddichio
 17: lima beans
[ 测试用例1成功 ]
-------------------------------------------
[ 开始测试用例2 ]
全部线程启动完毕
Read Thread 4 读出counter大小: 0
Read Thread 5 读出counter大小: 0
Read Thread 1 读出counter大小: 0
Read Thread 2 读出counter大小: 0
Read Thread 3 读出counter大小: 0
Write Thread 1 增加counter的大小为: 1
Write Thread 2 增加counter的大小为: 2
Write Thread 6 增加counter的大小为: 3
Write Thread 3 增加counter的大小为: 4
Write Thread 4 增加counter的大小为: 5
Write Thread 9 增加counter的大小为: 6
Write Thread 5 增加counter的大小为: 7
Write Thread 7 增加counter的大小为: 8
Write Thread 8 增加counter的大小为: 9
Write Thread 10 增加counter的大小为: 10
Read Thread 7 读出counter大小: 10
Read Thread 6 读出counter大小: 10
Read Thread 9 读出counter大小: 10
Read Thread 10 读出counter大小: 10
Read Thread 8 读出counter大小: 10
counter 最终大小为: 10
总用时为: 12 s
[ 测试用例2成功 ]
-------------------------------------------
[ 开始测试用例3 ]
全部线程启动完毕
Read lock reenter Read lock Thread 3 读出counter大小: 0
Read lock reenter Read lock Thread 5 读出counter大小: 0
Read lock reenter Read lock Thread 4 读出counter大小: 0
Read lock reenter Read lock Thread 1 读出counter大小: 0
Read lock reenter Read lock Thread 2 读出counter大小: 0
Read lock reenter Read lock Thread 3 读出counter大小: 0
Read lock reenter Read lock Thread 5 读出counter大小: 0
Read lock reenter Read lock Thread 1 读出counter大小: 0
Read lock reenter Read lock Thread 4 读出counter大小: 0
Read lock reenter Read lock Thread 2 读出counter大小: 0
Read lock reenter Write lock Thread 2 增加counter的大小为: 1
Read lock reenter Write lock Thread 2 读出counter大小: 1
Read lock reenter Write lock Thread 1 增加counter的大小为: 2
Read lock reenter Write lock Thread 1 读出counter大小: 2
Write lock reenter Write lock Thread 4 增加counter的大小为: 3
Write lock reenter Write lock Thread 4 增加counter的大小为: 4
Read lock reenter Write lock Thread 5 增加counter的大小为: 5
Read lock reenter Write lock Thread 5 读出counter大小: 5
Write lock reenter Write lock Thread 3 增加counter的大小为: 6
Write lock reenter Write lock Thread 3 增加counter的大小为: 7
Write lock reenter Write lock Thread 1 增加counter的大小为: 8
Write lock reenter Write lock Thread 1 增加counter的大小为: 9
Write lock reenter Write lock Thread 5 增加counter的大小为: 10
Write lock reenter Write lock Thread 5 增加counter的大小为: 11
Write lock reenter Write lock Thread 2 增加counter的大小为: 12
Write lock reenter Write lock Thread 2 增加counter的大小为: 13
Read lock reenter Write lock Thread 3 增加counter的大小为: 14
Read lock reenter Write lock Thread 3 读出counter大小: 14
Read lock reenter Write lock Thread 4 增加counter的大小为: 15
Read lock reenter Write lock Thread 4 读出counter大小: 15
Read lock reenter Read lock Thread 6 读出counter大小: 15
Read lock reenter Read lock Thread 9 读出counter大小: 15
Read lock reenter Read lock Thread 7 读出counter大小: 15
Read lock reenter Read lock Thread 8 读出counter大小: 15
Read lock reenter Read lock Thread 10 读出counter大小: 15
Read lock reenter Read lock Thread 7 读出counter大小: 15
Read lock reenter Read lock Thread 6 读出counter大小: 15
Read lock reenter Read lock Thread 9 读出counter大小: 15
Read lock reenter Read lock Thread 8 读出counter大小: 15
Read lock reenter Read lock Thread 10 读出counter大小: 15
counter 最终大小为: 15
总用时为: 24 s
[ 测试用例3成功 ]
*/