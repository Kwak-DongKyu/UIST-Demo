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

    // n명 지원: 현재 햅틱 오너(오케스트레이터에서 세팅)
    private HandshakeAgent boundAgent;

    // HapticRendering2 클래스 맨 위 아무 곳(헤더 근처)에 추가
    [Header("Agent Gate")]
    public bool gateByAgentState = true;   // false면 에이전트 상태 무시(디버그용)
    public float warmupSecs = 0.25f;       // 시작 직전 HandShake_on==true 기다리는 시간
    public float gateGraceSecs = 0.40f;    // 시작 후 이 시간 지나서만 조기정지 판단
    public int idleFramesToStop = 3;     // 연속 false N프레임 시 중단

    [Header("Start Gate")]
    public bool requireCalibration = true;   // ← true면 Z로 캘리브레이션 완료 전엔 시작 금지
                                             // HapticRendering2 클래스 맨 위에 추가
    [Header("Manual Profiles (Keyboard)")]
    public KeyCode weakKey = KeyCode.Alpha1;  // 1키: 약
    public KeyCode middleKey = KeyCode.Alpha2;  // 2키: 중
    public KeyCode strongKey = KeyCode.Alpha3;  // 3키: 강

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
                t1 += baseEncLeft;   // 절대 좌표(베이스 오프셋 적용)
                t2 += baseEncRight;
            }
            else
            {
                // ★ 캘리브레이션 전: 현재 위치 기준 상대 목표로 보냄
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
        // 수동 U/I/O/P
        if (Input.GetKeyDown(KeyCode.U)) { fromUiOp = true; SendCommand('a', encoderLeft + nudgeCounts, encoderRight); }
        if (Input.GetKeyDown(KeyCode.I)) { fromUiOp = true; SendCommand('a', encoderLeft - nudgeCounts, encoderRight); }
        if (Input.GetKeyDown(KeyCode.O)) { fromUiOp = true; SendCommand('a', encoderLeft, encoderRight + nudgeCounts); }
        if (Input.GetKeyDown(KeyCode.P)) { fromUiOp = true; SendCommand('a', encoderLeft, encoderRight - nudgeCounts); }

        // 정지 토글
        if (Input.GetKeyDown(KeyCode.X)) { stopping = !stopping; SendStopCommand(); }

        if (Input.GetKeyDown(KeyCode.I)) { if(calibrated) SendCommand('a', 0, 0); }

        // 캘리브레이션
        if (Input.GetKeyDown(KeyCode.Z))
        {
            lock (this) { baseEncLeft = encoderLeft; baseEncRight = encoderRight; }
            calibrated = true;
            Debug.Log($"Calibration Done. baseLeft={baseEncLeft}, baseRight={baseEncRight}");
        }

        // 디버그: H키 → 기본 Weak로 시작
        if (Input.GetKeyDown(handshakeKey))
        {
            if (!requireCalibration || calibrated)
                StartHandshakeWeak();
            else
                Debug.LogWarning("[Haptics] Ignored H: not calibrated. Press 'Z' first.");
        }

        // 바운드 에이전트가 Idle로 돌아가면 종료
        //if (boundAgent && handshakeCo != null && !boundAgent.HandShake_on)
        //    StopHandshakeNow();
        // 수동 프로필 재생 (1/2/3)
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
    // ★ 프로필별 시작 함수
    public void StartHandshakeWeak() { stopping = false; StartProfileRoutine(HandshakeRoutineWeak()); }
    public void StartHandshakeMiddle() { stopping = false; StartProfileRoutine(HandshakeRoutineMiddle()); }
    public void StartHandshakeStrong() { stopping = false; StartProfileRoutine(HandshakeRoutineStrong()); }


    // 프로필 enum으로 시작(오케스트레이터가 호출)
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
        // stopping 플래그는 굳이 true로 유지할 필요 없음. 다음 시작에 방해되니 false로.
        stopping = false;
    }

    // 공통 시작 헬퍼
    private void StartProfileRoutine(IEnumerator routine)
    {
        // ★ 캘리브레이션 가드
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
    // 현재는 3개 모두 동일하게 복제. 나중에 네가 내부 수식/파라미터만 변경하면 됨.

    // Weak
    // Weak
    IEnumerator HandshakeRoutineWeak()
    {
        Debug.Log("weak 햅틱 렌더링 시작함");

        float T = handshakeDuration; if (T <= 0f) yield break;

        // (선택) 웜업: ON 될 시간을 잠깐 준다
        float warm = warmupSecs; // 예: 0.25f
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

            // x(t): 0~1s 3→4, 이후 4
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
        Debug.Log("현재 middle 햅틱 렌더링 시작함");

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

            // x(t): 0~1s 3→5, 1~2s 5→4, 이후 4
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
        Debug.Log("strong 햅틱 렌더링 시작함");

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

            // x(t): 0~1s 3→5, 1~2s 5→3, 2~2.5s 3→4, 이후 4
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
    // HapticRendering2 안에 추가
    private IEnumerator ReturnToBaseAndStop(float settleSeconds = 0.12f)
    {
        // 베이스로 복귀
        if (calibrated)
        {
            // 베이스(0,0)로: SendCommand에서 baseEnc를 더하므로 베이스 절대 위치로 감
            SendCommand('a', 0, 0);
        }
        else
        {
            // 캘리브레이션 전이라면 현재 위치에서 베이스까지 상대 이동
            long dL, dR;
            lock (this)
            {
                dL = baseEncLeft - encoderLeft;
                dR = baseEncRight - encoderRight;
            }
            SendCommand('a', dL, dR);
        }

        // 약간의 정착 시간
        float t = 0f;
        while (t < settleSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // 모터 정지 신호
        SendStopCommand();
    }


}
