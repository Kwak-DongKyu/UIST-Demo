using System.Collections;
using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class HapticRendering2 : MonoBehaviour
{
    // --- Serial / Encoder ---
    [Header("Serial")]
    [SerializeField] private string comPort = "COM12";
    [SerializeField] private int baudRate = 115200;

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool isRunning;

    private long encoderLeft;
    private long encoderRight;

    // --- Calibration ---
    private long baseEncLeft = 0;
    private long baseEncRight = 0;
    private bool calibrated = false;

    [Header("Manual Nudge (encoder counts)")]
    [SerializeField] private int nudgeCounts = 100;
    private bool fromUiOp = false;

    [Header("Handshake")]
    public float ENCODER_PER_MM = 90.8f;
    public float handshakeDuration = 4.0f;
    public KeyCode handshakeKey = KeyCode.H;

    private Coroutine handshakeCo;
    private bool stopping = false; // X key toggle

    // n�� ����: ���� ��ƽ ����(���ɽ�Ʈ�����Ϳ��� ����)
    private HandshakeAgent boundAgent;

    // HapticRendering2 Ŭ���� �� �� �ƹ� ��(��� ��ó)�� �߰�
    [Header("Agent Gate")]
    public bool gateByAgentState = true;   // false�� ������Ʈ ���� ����(����׿�)
    public float warmupSecs = 0.25f;       // ���� ���� HandShake_on==true ��ٸ��� �ð�
    public float gateGraceSecs = 0.40f;    // ���� �� �� �ð� �������� �������� �Ǵ�
    public int idleFramesToStop = 3;     // ���� false N������ �� �ߴ�

    [Header("Start Gate")]
    public bool requireCalibration = true;   // �� true�� Z�� Ķ���극�̼� �Ϸ� ���� ���� ����
                                             // HapticRendering2 Ŭ���� �� ���� �߰�
    [Header("Manual Profiles (Keyboard)")]
    public KeyCode weakKey = KeyCode.Alpha1;  // 1Ű: ��
    public KeyCode middleKey = KeyCode.Alpha2;  // 2Ű: ��
    public KeyCode strongKey = KeyCode.Alpha3;  // 3Ű: ��

    // --- Serial Read Thread ---
    void ReadSerial()
    {
        while (isRunning)
        {
            try
            {
                if (serialPort != null && serialPort.IsOpen && serialPort.BytesToRead >= 12)
                {
                    int startByte = serialPort.ReadByte();
                    if (startByte == 0xAA)
                    {
                        byte[] leftBytes = new byte[4];
                        byte[] rightBytes = new byte[4];
                        byte[] fsrBytes = new byte[2];
                        serialPort.Read(leftBytes, 0, 4);
                        serialPort.Read(rightBytes, 0, 4);
                        serialPort.Read(fsrBytes, 0, 2);
                        int endByte = serialPort.ReadByte();
                        if (endByte == 0x55)
                        {
                            long leftVal = System.BitConverter.ToInt32(leftBytes, 0);
                            long rightVal = System.BitConverter.ToInt32(rightBytes, 0);
                            lock (this)
                            {
                                encoderLeft = leftVal;
                                encoderRight = rightVal;
                            }
                        }
                    }
                }
            }
            catch { /* ignore timeouts */ }
        }
    }

    // --- Commands ---
    private void SendCommand(char cmd, long t1, long t2)
    {
        Debug.Log(t1 + "," + t2);
        if (!fromUiOp)
        {
            if (calibrated)
            {
                t1 += baseEncLeft;   // ���� ��ǥ(���̽� ������ ����)
                t2 += baseEncRight;
            }
            else
            {
                // �� Ķ���극�̼� ��: ���� ��ġ ���� ��� ��ǥ�� ����
                lock (this)
                {
                    t1 += encoderLeft;
                    t2 += encoderRight;
                }
            }
        }

        string msg = $"{cmd},{t1},{t2}\n";
        if (serialPort != null && serialPort.IsOpen)
            serialPort.Write(msg);
        fromUiOp = false;
    }


    private void SendStopCommand()
    {
        if (serialPort != null && serialPort.IsOpen)
            serialPort.Write("b\n");
    }

    // --- Lifecycle ---
    void Start()
    {
        try
        {
            serialPort = new SerialPort(comPort, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 100;
            serialPort.Open();
            Debug.Log("Serial Opened: " + comPort);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Serial Open Failed: " + e.Message);
        }

        isRunning = true;
        readThread = new Thread(ReadSerial);
        readThread.Start();
    }

    void Update()
    {
        // ���� U/I/O/P
        if (Input.GetKeyDown(KeyCode.U)) { fromUiOp = true; SendCommand('a', encoderLeft + nudgeCounts, encoderRight); }
        if (Input.GetKeyDown(KeyCode.I)) { fromUiOp = true; SendCommand('a', encoderLeft - nudgeCounts, encoderRight); }
        if (Input.GetKeyDown(KeyCode.O)) { fromUiOp = true; SendCommand('a', encoderLeft, encoderRight + nudgeCounts); }
        if (Input.GetKeyDown(KeyCode.P)) { fromUiOp = true; SendCommand('a', encoderLeft, encoderRight - nudgeCounts); }

        // ���� ���
        if (Input.GetKeyDown(KeyCode.X)) { stopping = !stopping; SendStopCommand(); }

        if (Input.GetKeyDown(KeyCode.I)) { if(calibrated) SendCommand('a', 0, 0); }

        // Ķ���극�̼�
        if (Input.GetKeyDown(KeyCode.Z))
        {
            lock (this) { baseEncLeft = encoderLeft; baseEncRight = encoderRight; }
            calibrated = true;
            Debug.Log($"Calibration Done. baseLeft={baseEncLeft}, baseRight={baseEncRight}");
        }

        // �����: HŰ �� �⺻ Weak�� ����
        if (Input.GetKeyDown(handshakeKey))
        {
            if (!requireCalibration || calibrated)
                StartHandshakeWeak();
            else
                Debug.LogWarning("[Haptics] Ignored H: not calibrated. Press 'Z' first.");
        }

        // �ٿ�� ������Ʈ�� Idle�� ���ư��� ����
        //if (boundAgent && handshakeCo != null && !boundAgent.HandShake_on)
        //    StopHandshakeNow();
        // ���� ������ ��� (1/2/3)
        if (Input.GetKeyDown(weakKey))
        {
            if (!requireCalibration || calibrated)
            {
                Debug.Log("[Haptics] Manual: WEAK (1)");
                StartHandshakeWeak();
            }
            else Debug.LogWarning("[Haptics] Not calibrated. Press 'Z' first.");
        }

        if (Input.GetKeyDown(middleKey))
        {
            if (!requireCalibration || calibrated)
            {
                Debug.Log("[Haptics] Manual: MIDDLE (2)");
                StartHandshakeMiddle();
            }
            else Debug.LogWarning("[Haptics] Not calibrated. Press 'Z' first.");
        }

        if (Input.GetKeyDown(strongKey))
        {
            if (!requireCalibration || calibrated)
            {
                Debug.Log("[Haptics] Manual: STRONG (3)");
                StartHandshakeStrong();
            }
            else Debug.LogWarning("[Haptics] Not calibrated. Press 'Z' first.");
        }
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        if (readThread != null && readThread.IsAlive) readThread.Join();
        if (serialPort != null && serialPort.IsOpen) serialPort.Close();
    }

    // --- Public API for Orchestrator ---
    public void BindAgent(HandshakeAgent agent) => boundAgent = agent;
    public bool IsRunning => handshakeCo != null;
    // �� �����ʺ� ���� �Լ�
    public void StartHandshakeWeak() { stopping = false; StartProfileRoutine(HandshakeRoutineWeak()); }
    public void StartHandshakeMiddle() { stopping = false; StartProfileRoutine(HandshakeRoutineMiddle()); }
    public void StartHandshakeStrong() { stopping = false; StartProfileRoutine(HandshakeRoutineStrong()); }


    // ������ enum���� ����(���ɽ�Ʈ�����Ͱ� ȣ��)
    public void StartHandshakeForProfile(HapticProfile profile)
    {
        switch (profile)
        {
            case HapticProfile.Middle: StartHandshakeMiddle(); break;
            case HapticProfile.Strong: StartHandshakeStrong(); break;
            default: StartHandshakeWeak(); break;
        }
    }

    public void StopHandshakeNow()
    {
        if (handshakeCo != null)
        {
            StopCoroutine(handshakeCo);
            handshakeCo = null;
        }
        SendStopCommand();
        // stopping �÷��״� ���� true�� ������ �ʿ� ����. ���� ���ۿ� ���صǴ� false��.
        stopping = false;
    }

    // ���� ���� ����
    private void StartProfileRoutine(IEnumerator routine)
    {
        // �� Ķ���극�̼� ����
        if (requireCalibration && !calibrated)
        {
            Debug.LogWarning("[Haptics] Calibration required. Press 'Z' to calibrate before starting.");
            return;
        }

        stopping = false;
        if (handshakeCo != null) StopCoroutine(handshakeCo);
        handshakeCo = StartCoroutine(routine);
    }

    // --- Handshake motion logic (Weak/Middle/Strong)
    // ����� 3�� ��� �����ϰ� ����. ���߿� �װ� ���� ����/�Ķ���͸� �����ϸ� ��.

    // Weak
    // Weak
    IEnumerator HandshakeRoutineWeak()
    {
        Debug.Log("weak ��ƽ ������ ������");

        float T = handshakeDuration; if (T <= 0f) yield break;

        // (����) ����: ON �� �ð��� ��� �ش�
        float warm = warmupSecs; // ��: 0.25f
        while (warm > 0f && boundAgent && !boundAgent.HandShake_on)
        {
            warm -= Time.deltaTime;
            yield return null;
        }

        float t = 0f;
        while (t < T)
        {
            if (stopping) { SendStopCommand(); t += Time.deltaTime; yield return null; continue; }

            // y(t)
            float y = (t <= 0.5f) ? Mathf.Lerp(0f, 4f, t / 0.5f)
                    : (t <= 3.5f) ? 4f
                    : Mathf.Lerp(4f, 0f, (t - 3.5f) / 0.5f);

            // x(t): 0~1s 3��4, ���� 4
            float x = (t <= 1f) ? Mathf.Lerp(3f, 4f, t / 1f) : 4f;

            float fingerTotal = (y <= 0f) ? 0f : (y < 1f ? y : 1f);
            float ratio = (x <= 3f) ? 0f : (x >= 5f ? 1f : (x - 3f) / 2f);

            float A_mm = fingerTotal * (1f - ratio) * 5f;
            float B_mm = fingerTotal * ratio * 5f;

            long A_enc = (long)(A_mm * ENCODER_PER_MM);
            long B_enc = (long)(B_mm * ENCODER_PER_MM);

            SendCommand('a', A_enc, B_enc);

            t += Time.deltaTime;
            yield return null;
        }
        SendStopCommand();
        yield return StartCoroutine(ReturnToBaseAndStop());
        handshakeCo = null;
    }



    // Middle
    // Middle
    IEnumerator HandshakeRoutineMiddle()
    {
        Debug.Log("���� middle ��ƽ ������ ������");

        float T = handshakeDuration; if (T <= 0f) yield break;

        float warm = warmupSecs;
        while (warm > 0f && boundAgent && !boundAgent.HandShake_on)
        {
            warm -= Time.deltaTime;
            yield return null;
        }

        float t = 0f;
        while (t < T)
        {
            if (stopping) { SendStopCommand(); t += Time.deltaTime; yield return null; continue; }

            // y(t)
            float y = (t <= 0.5f) ? Mathf.Lerp(0f, 6f, t / 0.5f)
                    : (t <= 3.5f) ? 6f
                    : Mathf.Lerp(6f, 0f, (t - 3.5f) / 0.5f);

            // x(t): 0~1s 3��5, 1~2s 5��4, ���� 4
            float x = (t <= 1f) ? Mathf.Lerp(3f, 5f, t / 1f)
                   : (t <= 2f) ? Mathf.Lerp(5f, 4f, (t - 1f) / 1f)
                   : 4f;

            float fingerTotal = (y <= 0f) ? 0f : (y < 1f ? y : 1f);
            float ratio = (x <= 3f) ? 0f : (x >= 5f) ? 1f : (x - 3f) / 2f;

            float A_mm = fingerTotal * (1f - ratio) * 5f;
            float B_mm = fingerTotal * ratio * 5f;

            long A_enc = (long)(A_mm * ENCODER_PER_MM);
            long B_enc = (long)(B_mm * ENCODER_PER_MM);

            SendCommand('a', A_enc, B_enc);

            t += Time.deltaTime;
            yield return null;
        }
        SendStopCommand();
        yield return StartCoroutine(ReturnToBaseAndStop());
        handshakeCo = null;
    }



    // Strong
    // Strong
    IEnumerator HandshakeRoutineStrong()
    {
        Debug.Log("strong ��ƽ ������ ������");

        float T = handshakeDuration; if (T <= 0f) yield break;

        float warm = warmupSecs;
        while (warm > 0f && boundAgent && !boundAgent.HandShake_on)
        {
            warm -= Time.deltaTime;
            yield return null;
        }

        float t = 0f;
        while (t < T)
        {
            if (stopping) { SendStopCommand(); t += Time.deltaTime; yield return null; continue; }

            // y(t)
            float y = (t <= 0.5f) ? Mathf.Lerp(0f, 8f, t / 0.5f)
                    : (t <= 3.5f) ? 8f
                    : Mathf.Lerp(8f, 0f, (t - 3.5f) / 0.5f);

            // x(t): 0~1s 3��5, 1~2s 5��3, 2~2.5s 3��4, ���� 4
            float x = (t <= 1f) ? Mathf.Lerp(3f, 5f, t / 1f)
                   : (t <= 2f) ? Mathf.Lerp(5f, 3f, (t - 1f) / 1f)
                   : (t <= 2.5f) ? Mathf.Lerp(3f, 4f, (t - 2f) / 0.5f)
                   : 4f;

            float fingerTotal = (y <= 0f) ? 0f : (y < 1f ? y : 1f);
            float ratio = (x <= 3f) ? 0f : (x >= 5f) ? 1f : (x - 3f) / 2f;

            float A_mm = fingerTotal * (1f - ratio) * 5f;
            float B_mm = fingerTotal * ratio * 5f;

            long A_enc = (long)(A_mm * ENCODER_PER_MM);
            long B_enc = (long)(B_mm * ENCODER_PER_MM);

            SendCommand('a', A_enc, B_enc);

            t += Time.deltaTime;
            yield return null;
        }
        SendStopCommand();
        yield return StartCoroutine(ReturnToBaseAndStop());
        handshakeCo = null;
    }
    // HapticRendering2 �ȿ� �߰�
    private IEnumerator ReturnToBaseAndStop(float settleSeconds = 0.12f)
    {
        // ���̽��� ����
        if (calibrated)
        {
            // ���̽�(0,0)��: SendCommand���� baseEnc�� ���ϹǷ� ���̽� ���� ��ġ�� ��
            SendCommand('a', 0, 0);
        }
        else
        {
            // Ķ���극�̼� ���̶�� ���� ��ġ���� ���̽����� ��� �̵�
            long dL, dR;
            lock (this)
            {
                dL = baseEncLeft - encoderLeft;
                dR = baseEncRight - encoderRight;
            }
            SendCommand('a', dL, dR);
        }

        // �ణ�� ���� �ð�
        float t = 0f;
        while (t < settleSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // ���� ���� ��ȣ
        SendStopCommand();
    }


}
