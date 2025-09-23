#include <Encoder.h>

// 핀 설정
int m1_en = 9; 
int m2_en = 5; 
int m1_in1 = 12; 
int m1_in2 = 13; 
int m2_in1 = 8; 
int m2_in2 = 6; 

int FSRraw;

// 엔코더 설정op[ko]
Encoder knobLeft(0, 1);  // 모터1
Encoder knobRight(2, 3); // 모터2

long target_m1 = 0;
long target_m2 = 0;

bool mact = false;

// 현재 위치 변수
long positionLeft = 0;
long positionRight = 0;

void setup() {
  Serial.begin(115200);
  
  pinMode(m1_en, OUTPUT);
  pinMode(m1_in1, OUTPUT);
  pinMode(m1_in2, OUTPUT);
  pinMode(m2_en, OUTPUT);
  pinMode(m2_in1, OUTPUT);
  pinMode(m2_in2, OUTPUT);

  stopMotor(m1_en, m1_in1, m1_in2);
  stopMotor(m2_en, m2_in1, m2_in2);
}

void loop() {
  FSRraw = analogRead(A0);
  long newLeft = knobLeft.read();
  long newRight = -knobRight.read();

  // 엔코더 값 전송 (바이너리)
  sendEncoderValues(newLeft, newRight, FSRraw);

  // 명령 수신 처리
  receiveCommand(newLeft, newRight);

  // 모터 제어 로직
  if(mact)  controlMotor(newLeft, newRight);
  else {
    digitalWrite(m1_in1, LOW);
    digitalWrite(m1_in2, LOW);
    digitalWrite(m2_in1, LOW);
    digitalWrite(m2_in2, LOW);
    analogWrite(m1_en, 0);
    analogWrite(m2_en, 0);
  }
}

void receiveCommand(long currentLeft, long currentRight) {
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    // 명령 포맷: 
    // 'a,1000,2000'  -> 'a' 명령, target_m1=1000, target_m2=2000
    // 'b' -> 모터 정지 명령
    cmd.trim();
    if (cmd.length() > 0) {
      if (cmd.charAt(0) == 'a') {
        mact = true;
        // 파싱
        // 포맷: a,<target_m1>,<target_m2>
        int firstComma = cmd.indexOf(',', 1);
        int secondComma = cmd.indexOf(',', firstComma+1);

        if (firstComma > 0 && secondComma > 0) {
          long t1 = cmd.substring(firstComma+1, secondComma).toInt();
          long t2 = cmd.substring(secondComma+1).toInt();
          target_m1 = t1; 
          target_m2 = t2; 
        }
      } else if (cmd.charAt(0) == 'b') {
        // 모터 정지
        mact = false;
      }
      else if (cmd.charAt(0) == 's') {
        mact = false;
        fullyStopMotors(); 
      }
      else if (cmd.charAt(0) == 'l') {
        int commaIndex = cmd.indexOf(',');
        if (commaIndex > 0) {
          long ts = cmd.substring(commaIndex+1).toInt();
          // latency 응답을 이진 패킷으로 전송: 시작 마커 0xAC, 4바이트 타임스탬프, 종료 마커 0x55
          Serial.write(0xAC);
          Serial.write((byte *) &ts, sizeof(ts));  // 4바이트 전송
          Serial.write(0x55);
        }
      }
    }
  }
}

void controlMotor(long newLeft, long newRight) {
  // 모터1 제어
  if (newLeft < target_m1) {
    moveMotor(m1_en, m1_in1, m1_in2, true); // 증가 방향
  } else if (newLeft > target_m1) {
    moveMotor(m1_en, m1_in1, m1_in2, false); // 감소 방향
  } else {
    stopMotor(m1_en, m1_in1, m1_in2); 
  }

  // 모터2 제어
  if (newRight < target_m2) {
    moveMotor(m2_en, m2_in1, m2_in2, true); // false면 in1=LOW,in2=HIGH, 증가
  } else if (newRight > target_m2) {
    // 감소 필요 -> in1=HIGH,in2=LOW
    moveMotor(m2_en, m2_in1, m2_in2, false);
  } else {
    stopMotor(m2_en, m2_in1, m2_in2);
  }
}

void sendEncoderValues(long leftVal, long rightVal, int fsrVal) {
  // 패킷: 0xAA start, left(4byte), right(4byte), 0x55 end
  Serial.write(0xAA);
  Serial.write((byte*)&leftVal, sizeof(leftVal));
  Serial.write((byte*)&rightVal, sizeof(rightVal));
  Serial.write((byte*)&fsrVal, sizeof(fsrVal));
  Serial.write(0x55);
}

void moveMotor(int pwmPin, int dirPin1, int dirPin2, bool forward) {
  if (forward) {
    digitalWrite(dirPin1, HIGH);
    digitalWrite(dirPin2, LOW);
  } else {
    digitalWrite(dirPin1, LOW);
    digitalWrite(dirPin2, HIGH);
  }
  analogWrite(pwmPin, 200); // PWM 값 
}

void stopMotor(int pwmPin, int dirPin1, int dirPin2) {
  digitalWrite(dirPin1, LOW);
  digitalWrite(dirPin2, LOW);
  analogWrite(pwmPin, 0);
}

void fullyStopMotors() {
  // IN1, IN2 모두 LOW + PWM 0 → 완전 무전류 정지
  digitalWrite(m1_in1, LOW);
  digitalWrite(m1_in2, LOW);
  analogWrite(m1_en, 0);

  digitalWrite(m2_in1, LOW);
  digitalWrite(m2_in2, LOW);
  analogWrite(m2_en, 0);
}
