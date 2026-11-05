;; Direct-sampling guest. Host layout (little-endian), also documented in WasmGuestLayout:
;;   0  f64 time
;;   8  f32 speed
;;  12  f32 angle
;;  16  i32 width
;;  20  i32 height
;;  24  i32 colorCount
;;  28  i32 pointCount
;;  32  u32 colors[8] as 0x00RRGGBB
;;  64  points: f32 x, f32 y
;;  then u32 0x00RRGGBB per point
;;
;; Three sines over the rotated LED position, then a blend through the color list.
(module
  (memory (export "memory") 1 32)

  (func $floor (param $v f32) (result f32)
    (local $truncated f32)
    (local.set $truncated (f32.trunc (local.get $v)))
    (if (result f32) (f32.lt (local.get $v) (local.get $truncated))
      (then (f32.sub (local.get $truncated) (f32.const 1)))
      (else (local.get $truncated))))

  ;; Taylor sine after wrapping into [-pi, pi]. Enough for an LED field.
  (func $sin (param $x f32) (result f32)
    (local $x2 f32)
    (local $x3 f32)
    (local $x5 f32)
    (local $x7 f32)
    (local.set $x
      (f32.sub
        (local.get $x)
        (f32.mul
          (f32.const 6.28318548)
          (call $floor (f32.div (local.get $x) (f32.const 6.28318548))))))
    (if (f32.gt (local.get $x) (f32.const 3.14159274))
      (then (local.set $x (f32.sub (local.get $x) (f32.const 6.28318548)))))
    (local.set $x2 (f32.mul (local.get $x) (local.get $x)))
    (local.set $x3 (f32.mul (local.get $x2) (local.get $x)))
    (local.set $x5 (f32.mul (local.get $x3) (local.get $x2)))
    (local.set $x7 (f32.mul (local.get $x5) (local.get $x2)))
    (f32.add
      (f32.sub
        (local.get $x)
        (f32.div (local.get $x3) (f32.const 6)))
      (f32.sub
        (f32.div (local.get $x5) (f32.const 120))
        (f32.div (local.get $x7) (f32.const 5040)))))

  (func $clamp_byte (param $value i32) (result i32)
    (if (result i32) (i32.lt_s (local.get $value) (i32.const 0))
      (then (i32.const 0))
      (else
        (if (result i32) (i32.gt_s (local.get $value) (i32.const 255))
          (then (i32.const 255))
          (else (local.get $value))))))

  (func $lerp (param $from i32) (param $to i32) (param $fraction f32) (result i32)
    (call $clamp_byte
      (i32.trunc_f32_s
        (f32.add
          (f32.convert_i32_s (local.get $from))
          (f32.add
            (f32.mul
              (f32.convert_i32_s (i32.sub (local.get $to) (local.get $from)))
              (local.get $fraction))
            (f32.const 0.5))))))

  (func $load_color (param $index i32) (result i32)
    (i32.load
      (i32.add
        (i32.const 32)
        (i32.mul (local.get $index) (i32.const 4)))))

  ;; u in 0..1 across the color list written at address 32.
  (func $gradient (param $u f32) (result i32)
    (local $count i32)
    (local $scaled f32)
    (local $index i32)
    (local $fraction f32)
    (local $from i32)
    (local $to i32)
    (local.set $count (i32.load (i32.const 24)))
    (if (i32.lt_s (local.get $count) (i32.const 1))
      (then (return (i32.const 0x00ffffff))))
    (if (i32.eq (local.get $count) (i32.const 1))
      (then (return (call $load_color (i32.const 0)))))
    (local.set $u (f32.min (f32.const 1) (f32.max (f32.const 0) (local.get $u))))
    (local.set $scaled
      (f32.mul
        (local.get $u)
        (f32.convert_i32_s (i32.sub (local.get $count) (i32.const 1)))))
    (local.set $index (i32.trunc_f32_s (local.get $scaled)))
    (if (i32.ge_s (local.get $index) (i32.sub (local.get $count) (i32.const 1)))
      (then
        (local.set $index (i32.sub (local.get $count) (i32.const 2)))
        (local.set $fraction (f32.const 1)))
      (else
        (local.set $fraction
          (f32.sub
            (local.get $scaled)
            (f32.convert_i32_s (local.get $index))))))
    (local.set $from (call $load_color (local.get $index)))
    (local.set $to (call $load_color (i32.add (local.get $index) (i32.const 1))))
    (i32.or
      (i32.shl
        (call $lerp
          (i32.shr_u (local.get $from) (i32.const 16))
          (i32.shr_u (local.get $to) (i32.const 16))
          (local.get $fraction))
        (i32.const 16))
      (i32.or
        (i32.shl
          (call $lerp
            (i32.and (i32.shr_u (local.get $from) (i32.const 8)) (i32.const 255))
            (i32.and (i32.shr_u (local.get $to) (i32.const 8)) (i32.const 255))
            (local.get $fraction))
          (i32.const 8))
        (call $lerp
          (i32.and (local.get $from) (i32.const 255))
          (i32.and (local.get $to) (i32.const 255))
          (local.get $fraction)))))

  (func (export "sample") (result i32)
    (local $count i32)
    (local $index i32)
    (local $point i32)
    (local $out i32)
    (local $time f32)
    (local $angle f32)
    (local $sine f32)
    (local $cosine f32)
    (local $x f32)
    (local $y f32)
    (local $rotated_x f32)
    (local $rotated_y f32)
    (local $field f32)
    (local.set $count (i32.load (i32.const 28)))
    (local.set $out
      (i32.add
        (i32.const 64)
        (i32.mul (local.get $count) (i32.const 8))))
    (local.set $time
      (f32.demote_f64
        (f64.mul
          (f64.load (i32.const 0))
          (f64.promote_f32 (f32.load (i32.const 8))))))
    (local.set $angle
      (f32.mul
        (f32.load (i32.const 12))
        (f32.const 0.0174532924)))
    (local.set $sine (call $sin (local.get $angle)))
    (local.set $cosine (call $sin (f32.add (local.get $angle) (f32.const 1.57079637))))
    (loop $next
      (if (i32.lt_s (local.get $index) (local.get $count))
        (then
          (local.set $point
            (i32.add
              (i32.const 64)
              (i32.mul (local.get $index) (i32.const 8))))
          (local.set $x (f32.load (local.get $point)))
          (local.set $y (f32.load offset=4 (local.get $point)))
          (local.set $rotated_x
            (f32.sub
              (f32.mul (local.get $x) (local.get $cosine))
              (f32.mul (local.get $y) (local.get $sine))))
          (local.set $rotated_y
            (f32.add
              (f32.mul (local.get $x) (local.get $sine))
              (f32.mul (local.get $y) (local.get $cosine))))
          (local.set $field
            (f32.add
              (f32.add
                (call $sin
                  (f32.add
                    (f32.mul (local.get $rotated_x) (f32.const 0.05))
                    (local.get $time)))
                (call $sin
                  (f32.add
                    (f32.mul (local.get $rotated_y) (f32.const 0.08))
                    (f32.mul (local.get $time) (f32.const 0.7)))))
              (call $sin
                (f32.sub
                  (f32.mul
                    (f32.add (local.get $rotated_x) (local.get $rotated_y))
                    (f32.const 0.04))
                  (f32.mul (local.get $time) (f32.const 1.3))))))
          (i32.store
            (i32.add
              (local.get $out)
              (i32.mul (local.get $index) (i32.const 4)))
            (call $gradient
              (f32.div
                (f32.add (local.get $field) (f32.const 3))
                (f32.const 6))))
          (local.set $index (i32.add (local.get $index) (i32.const 1)))
          (br $next))))
    (i32.const 0)))
