import mediapipe as mp
import cv2
import numpy as np

class PoseEstimation:
    def __init__(self, static_image_mode=True, model_complexity=2, smooth_landmarks=True, enable_segmentation=False,
                 smooth_segmentation=True, min_detection_confidence=0.7, min_tracking_confidence=0.7):
        """Initializes a MediaPipe Pose object.

        Args:
          static_image_mode: Whether to treat the input images as a batch of static
            and possibly unrelated images, or a video stream. See details in
            https://solutions.mediapipe.dev/pose#static_image_mode.
          model_complexity: Complexity of the pose landmark model: 0, 1 or 2. See
            details in https://solutions.mediapipe.dev/pose#model_complexity.
          smooth_landmarks: Whether to filter landmarks across different input
            images to reduce jitter. See details in
            https://solutions.mediapipe.dev/pose#smooth_landmarks.
          enable_segmentation: Whether to predict segmentation mask. See details in
            https://solutions.mediapipe.dev/pose#enable_segmentation.
          smooth_segmentation: Whether to filter segmentation across different input
            images to reduce jitter. See details in
            https://solutions.mediapipe.dev/pose#smooth_segmentation.
          min_detection_confidence: Minimum confidence value ([0.0, 1.0]) for person
            detection to be considered successful. See details in
            https://solutions.mediapipe.dev/pose#min_detection_confidence.
          min_tracking_confidence: Minimum confidence value ([0.0, 1.0]) for the
            pose landmarks to be considered tracked successfully. See details in
            https://solutions.mediapipe.dev/pose#min_tracking_confidence.
        """
        mp_pose = mp.solutions.pose
        mp_drawing = mp.solutions.drawing_utils

        self.mp_pose = mp_pose
        self.mp_drawing = mp_drawing

        self.pose = mp_pose.Pose(
            static_image_mode=static_image_mode,
            model_complexity=model_complexity,
            smooth_landmarks=smooth_landmarks,
            enable_segmentation=enable_segmentation,
            smooth_segmentation=smooth_segmentation,
            min_detection_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence)
        
    def process_image(self, image):
        """Processes an image and returns the pose landmarks.

        Args:
          image: An RGB image represented as a NumPy array.

        Returns:
          A list of pose landmarks.
        """
        results = self.pose.process(image)
        
        if results.pose_landmarks:
            self.mp_drawing.draw_landmarks(
                image, results.pose_landmarks, self.mp_pose.POSE_CONNECTIONS)

            landmark_list = []
            for landmark in results.pose_landmarks.landmark:
                landmark_list.append([
                    landmark.x,
                    landmark.y,
                    landmark.z
                ])
            
            custom_skeleton = self.skeleton_transform(np.array(landmark_list))
            encode_data = self.encoder(custom_skeleton)
            
            return encode_data
    
    def skeleton_transform(self, mp_skeleton):
        mapping_table = [
            (0, 0), (2, 12), (3, 14), (4, 16), (5, 11), (6, 13), (7, 15), (9, 24), (10, 26), (11, 28), (12, 23), (13, 25), (14, 27), (15, 31), (16, 32)
        ]
        custom_skeleton = np.zeros((17, 3))
        custom_skeleton[1] = (((mp_skeleton[11] + mp_skeleton[12]) / 2) - mp_skeleton[0]) * 0.8 + mp_skeleton[0]
        custom_skeleton[8] = (((mp_skeleton[23] + mp_skeleton[24]) / 2) - ((mp_skeleton[11] + mp_skeleton[12]) / 2)) * 0.95 + ((mp_skeleton[11] + mp_skeleton[12]) / 2)
        for custom_id, mp_id in mapping_table:
            custom_skeleton[custom_id] = mp_skeleton[mp_id]
            
        return custom_skeleton
    
    def encoder(self, skeleton_data):
        data = ""
        for idx, skeletons in enumerate(skeleton_data):
            data += str(skeletons[0]) + " " + str(skeletons[1]) + " " + str(skeletons[2]) + ","
        data = data[:len(data)-1]
        
        return data
    
if __name__ == "__main__":
    pose_estimation = PoseEstimation()
    cap = cv2.VideoCapture(0)
    while cap.isOpened():
        ret, frame = cap.read()
        if not ret:
            break
        
        frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        pose_estimation.process_image(frame_rgb)
        
        cv2.imshow("frame", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break
            
    cap.release()
    cv2.destroyAllWindows()